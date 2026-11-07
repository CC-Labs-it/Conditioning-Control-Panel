using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// --fx-bench &lt;out.json&gt; [--fx-bench-size WxH]: what one frame of each Home effect costs on Skia's
    /// CPU backend (lane perf, Linux beta fix round, 2026-11-06). Headless Skia IS the raster path a
    /// machine without a GPU draws on (WSLg, VMs, old laptops), and it needs no display, so the numbers
    /// are the render thread's work per frame with no window on anybody's screen. The shell opens on
    /// Home at the owner's 1661x1002 (1x) by default; each case dirties what one tick of that effect
    /// dirties and times the composed frame (median of <see cref="Frames"/>). A 30 fps loop costs
    /// ms x 30 / 1000 of one core. Exit 0 with the JSON written.
    /// </summary>
    internal static class FxBench
    {
        internal const int Frames = 60;

        public static int Run(string outPath, string? size)
        {
            RenderProof.EnsureSetUp();
            RenderProof.Rendering = true;   // views draw from memory and never save a profile
            var (width, height) = ParseSize(size);
            var w = new MainShellWindow { Width = width, Height = height };
            try
            {
                w.Show();
                w.ShowTab("settings");
                for (int i = 0; i < 10; i++) Frame();
                var tab = w.FindControl<SettingsTabView>("SettingsTab");
                var mosaic = tab?.FindControl<AmbientFxCanvas>("MosaicFx");
                if (tab == null || mosaic == null) { Console.Error.WriteLine("fx-bench: Home is not showing"); return 3; }
                var marquee = tab.Marquee;

                var rows = new List<(string Case, double Median, double P90)>();
                void Case(string name, Action perturb)
                {
                    for (int i = 0; i < 5; i++) { perturb(); Frame(); }   // warm caches
                    var ms = new List<double>();
                    var sw = new Stopwatch();
                    for (int i = 0; i < Frames; i++)
                    {
                        perturb();
                        sw.Restart();
                        Frame();
                        ms.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    ms.Sort();
                    rows.Add((name, ms[ms.Count / 2], ms[(int)(ms.Count * 0.9)]));
                    Console.WriteLine($"{name,-28} {ms[ms.Count / 2],7:F2} ms median  {ms[(int)(ms.Count * 0.9)],7:F2} ms p90");
                }

                Case("idle", () => { });
                Case("marquee step", () => marquee.Transform.X -= MarqueeScroller.PixelsPerSecond / 30);
                Case("mosaic fog tick", () => mosaic.InvalidateVisual());
                Case("fog + marquee", () => { mosaic.InvalidateVisual(); marquee.Transform.X -= MarqueeScroller.PixelsPerSecond / 30; });
                Case("whole window", () => w.InvalidateVisual());

                // What an idle Home dirties by itself, painted by Avalonia's own overlay.
                w.RendererDiagnostics.DebugOverlays = global::Avalonia.Rendering.RendererDebugOverlays.DirtyRects;
                for (int i = 0; i < 3; i++) Frame();
                using (var shot = w.CaptureRenderedFrame()) shot?.Save(Path.ChangeExtension(outPath, ".dirty.png"));
                w.RendererDiagnostics.DebugOverlays = global::Avalonia.Rendering.RendererDebugOverlays.None;

                var json = JsonSerializer.Serialize(new
                {
                    schema = "ccp-fx-bench/1",
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    width, height,
                    frames = Frames,
                    cases = rows.Select(r => new { @case = r.Case, median_ms = Math.Round(r.Median, 2), p90_ms = Math.Round(r.P90, 2) }),
                }, new JsonSerializerOptions { WriteIndented = true });
                var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, json);
                return 0;
            }
            finally { w.Close(); Frame(); }
        }

        /// <summary>One composed frame: the render tick, then the jobs it posts.</summary>
        private static void Frame()
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        internal static (double W, double H) ParseSize(string? size)
        {
            var parts = size?.Split('x', 'X');
            if (parts is { Length: 2 } && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h) && w >= 400 && h >= 300)
                return (w, h);
            return (1661, 1002);
        }
    }
}
