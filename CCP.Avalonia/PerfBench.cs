using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// --perf-bench &lt;out.json&gt;: startup, per-tab switch latency and working set on the REAL windowing
    /// platform (headless numbers say nothing about startup). Cycles NavCheck's tab list through ShowTab, the
    /// call every rail handler makes, and times each switch to the next frame the render thread finished.
    /// Exit 0 with the JSON written; 3 on a tab that is missing or does not show; 4 on a timeout.
    /// Shape <c>ccp-perf-bench/1</c> is shared with the WPF head's twin (docs/avalonia-perf.html).
    /// </summary>
    internal static class PerfBench
    {
        /// <summary>Started on the first line of Program.Main.</summary>
        internal static readonly Stopwatch SinceMain = new();
        internal const int Cycles = 20;
        private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

        public static int Run(string outPath, string[]? tabs)
        {
            tabs ??= NavCheck.TabPanels;
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            using var cts = new CancellationTokenSource();
            var code = 1;
            Dispatcher.UIThread.Post(async () =>
            {
                try { code = await Measure(outPath, tabs); }
                catch (TimeoutException) { Console.Error.WriteLine("perf-bench: timed out waiting for a frame"); code = 4; }
                catch (Exception ex) { Console.Error.WriteLine("perf-bench: " + ex); code = 1; }
                finally { cts.Cancel(); }
            });
            Dispatcher.UIThread.MainLoop(cts.Token);
            return code;
        }

        /// <summary>Shows a tab by panel name the way the rail does; false when the panel is missing or stays hidden.</summary>
        internal static bool Show(MainShellWindow w, string panel)
        {
            w.ShowTab(panel.EndsWith("Tab", StringComparison.Ordinal) ? panel[..^3] : panel);
            return w.Named<Control>(panel)?.IsVisible == true;
        }

        private static async Task<int> Measure(string outPath, string[] tabs)
        {
            var w = new MainShellWindow();
            // Closed on every exit path before the loop ends: a shell still mapped at process exit crashed
            // the runtime's teardown (exit 139).
            try { return await Measure(w, outPath, tabs); }
            finally { w.Close(); }
        }

        private static async Task<int> Measure(MainShellWindow w, string outPath, string[] tabs)
        {
            var opened = new TaskCompletionSource();
            w.Opened += (_, _) => opened.TrySetResult();
            w.Show();
            await opened.Task.WaitAsync(FrameTimeout);
            var startupMs = await NextFrame(w, SinceMain);
            var sinceProcessMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;

            var samples = tabs.Select(_ => new List<double>()).ToArray();
            var sw = new Stopwatch();
            for (var cycle = 0; cycle <= Cycles; cycle++)       // cycle 0 is the discarded warm-up
                for (var i = 0; i < tabs.Length; i++)
                {
                    sw.Restart();
                    if (!Show(w, tabs[i]))
                    {
                        Console.Error.WriteLine($"perf-bench: tab {tabs[i]} is missing or did not show");
                        return 3;
                    }
                    var ms = await NextFrame(w, sw);
                    if (cycle > 0) samples[i].Add(ms);
                }

            for (var i = 0; i < 2; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            using var proc = Process.GetCurrentProcess();
            proc.Refresh();

            var json = JsonSerializer.Serialize(new
            {
                schema = "ccp-perf-bench/1",
                head = "avalonia",
                os = RuntimeInformation.OSDescription,
                runtime = RuntimeInformation.FrameworkDescription,
                startup_ms = Math.Round(startupMs, 1),
                startup_since_process_ms = Math.Round(sinceProcessMs, 1),
                working_set_mb = Math.Round(proc.WorkingSet64 / 1048576.0, 1),
                cycles = Cycles,
                tabs = tabs.Select((t, i) => new
                {
                    tab = t,
                    median_ms = Math.Round(Percentile(samples[i], 50), 2),
                    p95_ms = Math.Round(Percentile(samples[i], 95), 2),
                }).ToArray(),
            }, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(outPath, json);
            Console.WriteLine(json);
            return 0;
        }

        /// <summary>Elapsed ms when the batch carrying the pending visual changes has rendered.
        /// MediaContext processes layout and draw-list updates before committing this batch. An
        /// animation-frame callback first would wait for a separate pulse (which is throttled by
        /// outstanding composition), and can time an extra frame after the tab is already drawn.</summary>
        internal static async Task<double> NextFrame(TopLevel w, Stopwatch clock)
        {
            var compositor = ElementComposition.GetElementVisual(w)?.Compositor
                ?? throw new InvalidOperationException("the window has no compositor");
            var requestedMs = clock.Elapsed.TotalMilliseconds;
            await compositor.RequestCompositionBatchCommitAsync().Rendered
                .WaitAsync(FrameTimeout).ConfigureAwait(false);
            var renderedMs = clock.Elapsed.TotalMilliseconds;
            if (TabSwitchTiming.Enabled)
                Serilog.Log.Debug("[TabSwitchFrame] requested={RequestedMs} rendered={RenderedMs}", requestedMs, renderedMs);
            return renderedMs;
        }

        /// <summary>Median interpolates the middle pair; other percentiles are nearest-rank.</summary>
        internal static double Percentile(List<double> xs, int p)
        {
            if (xs.Count == 0) return double.NaN;
            var s = xs.OrderBy(x => x).ToArray();
            if (p == 50) return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
            return s[Math.Clamp((int)Math.Ceiling(p / 100.0 * s.Length) - 1, 0, s.Length - 1)];
        }
    }
}
