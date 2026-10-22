using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--overlay-bench [seconds]`: the measurement gate of the compositor decision
    /// (docs/avalonia-decisions.md 2026-10-22). Runs the worst per-effect stack - pink tint, spiral,
    /// bubbles, flash bursts, subliminals - on every screen and reports UI-thread latency (p95/max),
    /// missed frames per overlay window and process CPU, writes JSON, and exits 1 when
    /// <see cref="OverlayBenchMetrics.Failures"/> finds any (max latency > 100 ms, a stall >= 1 s).
    /// `--overlay-bench-stall` blocks the UI thread 1.5 s halfway, to prove the failing exit.
    /// Sandbox only (CCP_USERDATA_DIR): it seeds flash pictures and flips effect flags. Run it in a
    /// nested compositor (Xephyr/weston/kwin --virtual), never on the live desktop: it covers every screen.
    /// The frame probe requests a frame per overlay window, so it adds a little render load itself.
    /// </summary>
    internal static class OverlayBench
    {
        public static int Run(int seconds, bool injectStall, string? outPath)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")))
            {
                Console.Error.WriteLine("overlay-bench: refuses to run without a CCP_USERDATA_DIR sandbox");
                return 2;
            }
            seconds = Math.Clamp(seconds, 5, 3600);
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);

            var code = 1;
            var sw = Stopwatch.StartNew();
            var measuring = false;
            var latency = new List<double>();            // UI thread only
            var tracks = new Dictionary<Window, FrameTrack>();
            var proc = Process.GetCurrentProcess();
            var cpu0 = TimeSpan.Zero;
            double t0 = 0;
            var host = new Window();                      // never shown: only reaches Screens

            void TrackFrames(Window w, FrameTrack t) => w.RequestAnimationFrame(_ =>
            {
                if (!measuring || !w.IsVisible) return;
                t.Add(sw.Elapsed.TotalMilliseconds);
                TrackFrames(w, t);
            });
            void Scan()
            {
                foreach (var w in lifetime.Windows.ToList())
                {
                    if (tracks.ContainsKey(w) || w is not (TintOverlayWindow or SpiralOverlayWindow or BubbleOverlayWindow
                            or FlashOverlayWindow or SubliminalOverlayWindow) || !w.IsVisible) continue;
                    var t = new FrameTrack($"{w.GetType().Name}#{tracks.Count}@{w.Position.X},{w.Position.Y}");
                    tracks[w] = t;
                    TrackFrames(w, t);
                }
            }

            // UI-thread latency: posted from a pool timer, timed when the UI thread runs it.
            using var probe = new System.Threading.Timer(_ =>
            {
                var posted = sw.Elapsed.TotalMilliseconds;
                Dispatcher.UIThread.Post(() => { if (measuring) latency.Add(sw.Elapsed.TotalMilliseconds - posted); });
            }, null, 0, 20);

            DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    var screens = host.Screens.All;
                    Console.WriteLine($"overlay-bench: {seconds} s on {screens.Count} screen(s), DISPLAY={Environment.GetEnvironmentVariable("DISPLAY")}");
                    SeedFlashPictures();
                    var s = CoreSettings.Current;
                    s.DualMonitorEnabled = true;
                    s.GlobalTargetMonitor = s.PinkFilterTargetMonitor = s.SpiralTargetMonitor = MonitorTarget.All;
                    s.PinkFilterEnabled = s.SpiralEnabled = true;
                    s.BubblesFrequency = 60;

                    measuring = true;
                    t0 = sw.Elapsed.TotalMilliseconds;
                    cpu0 = proc.TotalProcessorTime;
                    CoreEngine.Start();
                    PinkFilterOverlay.Refresh(host);
                    SpiralOverlay.Refresh(host);          // decodes first, then shows itself
                    BubbleOverlay.Start(host, 60);
                    FlashOverlay.TriggerOnce(host, amount: 8);

                    var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    var n = 0;
                    tick.Tick += (_, _) =>
                    {
                        n++;
                        if (n % 4 == 0) SpiralOverlay.Refresh(host);   // idempotent once shown
                        if (n % 6 == 0) SubliminalOverlay.Show(host, "OVERLAY BENCH", 80);
                        if (n % 16 == 0) FlashOverlay.TriggerOnce(host, amount: 8);   // skipped while a burst is busy
                        Scan();
                    };
                    tick.Start();
                    if (injectStall)
                        DispatcherTimer.RunOnce(() => System.Threading.Thread.Sleep(1500), TimeSpan.FromSeconds(seconds / 2.0));
                    DispatcherTimer.RunOnce(() =>
                    {
                        try
                        {
                            tick.Stop();
                            measuring = false;
                            code = Report(latency, tracks.Values.ToList(), proc.TotalProcessorTime - cpu0,
                                sw.Elapsed.TotalMilliseconds - t0, screens.Count, injectStall, outPath);
                        }
                        catch (Exception e) { Console.Error.WriteLine("overlay-bench threw: " + e); code = 1; }
                        finally
                        {
                            Views.Windows.MainShellWindow.StopEngine();
                            PinkFilterOverlay.CloseAll(); SpiralOverlay.CloseAll(); BubbleOverlay.Stop();
                            SubliminalOverlay.CloseAll();
                            lifetime.Shutdown(code);
                        }
                    }, TimeSpan.FromSeconds(seconds));
                }
                catch (Exception e) { Console.Error.WriteLine("overlay-bench threw: " + e); lifetime.Shutdown(1); }
            }, TimeSpan.FromMilliseconds(1500));

            lifetime.Start(Array.Empty<string>());
            return code;
        }

        private static int Report(List<double> latency, List<FrameTrack> tracks, TimeSpan cpu, double wallMs,
            int screens, bool injectStall, string? outPath)
        {
            var fails = OverlayBenchMetrics.Failures(latency, tracks);
            var cpuPercent = wallMs > 0 ? cpu.TotalMilliseconds / wallMs * 100 : 0;   // 100 = one core
            var doc = new
            {
                screens,
                seconds = Math.Round(wallMs / 1000, 1),
                injectStall,
                uiLatencyMs = new
                {
                    samples = latency.Count,
                    p95 = Math.Round(OverlayBenchMetrics.Percentile(latency, 95), 1),
                    max = Math.Round(latency.Count == 0 ? 0 : latency.Max(), 1),
                },
                cpuPercentOfOneCore = Math.Round(cpuPercent, 1),
                cores = Environment.ProcessorCount,
                windows = tracks.Select(t => new { t.Name, t.Frames, t.Missed, maxGapMs = Math.Round(t.MaxGapMs, 1) }),
                thresholds = new { OverlayBenchMetrics.MaxUiLatencyMs, OverlayBenchMetrics.StallMs },
                pass = fails.Count == 0,
                failures = fails,
            };
            var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            var path = outPath ?? Path.Combine(CorePaths.UserData, "overlay-bench.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json);
            Console.WriteLine(json);
            Console.WriteLine($"overlay-bench: wrote {path}");
            Console.WriteLine(fails.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", fails));
            return fails.Count == 0 ? 0 : 1;
        }

        /// <summary>A fresh sandbox has no flash pictures; write a few plain ones so bursts draw.</summary>
        private static void SeedFlashPictures()
        {
            var dir = Path.Combine(CorePaths.EffectiveAssets, "images");
            Directory.CreateDirectory(dir);
            if (Directory.EnumerateFiles(dir).Any()) return;
            var colors = new[] { Colors.HotPink, Colors.MediumPurple, Colors.DeepSkyBlue, Colors.Gold };
            for (var i = 0; i < colors.Length; i++)
            {
                using var rtb = new RenderTargetBitmap(new PixelSize(800, 600));
                using (var ctx = rtb.CreateDrawingContext())
                    ctx.FillRectangle(new SolidColorBrush(colors[i]), new Rect(0, 0, 800, 600));
                rtb.Save(Path.Combine(dir, $"bench-{i}.png"));
            }
        }
    }
}
