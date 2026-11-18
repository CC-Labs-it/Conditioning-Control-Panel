using System;
using System.Diagnostics;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Diagnostics
{
    /// <summary>
    /// Opt-in measurement of the UI dispatcher's frame cadence (env <c>CCP_FRAME_PROBE=1</c>). The overlay
    /// compositor records its frames on this dispatcher, so the gap between two Rendering ticks is the
    /// overlay frame time a panel loop can steal. One log line every two seconds: frames a second, the
    /// 95th percentile and the longest gap, and whether the panel is standing down. Off costs nothing.
    /// </summary>
    internal static class FrameProbe
    {
        private const int WindowMs = 2000;
        private static readonly Stopwatch Watch = new();
        private static readonly double[] Gaps = new double[1024];
        private static int _n;
        private static double _last, _windowStart;
        private static bool _started;

        internal static void StartIfAsked()
        {
            if (_started || Environment.GetEnvironmentVariable("CCP_FRAME_PROBE") != "1") return;
            _started = true;
            Watch.Start();
            CompositionTarget.Rendering += OnRendering;
            App.Logger?.Information("FrameProbe: on");
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            double now = Watch.Elapsed.TotalMilliseconds;
            if (_last > 0 && _n < Gaps.Length) Gaps[_n++] = now - _last;
            _last = now;
            if (now - _windowStart < WindowMs) return;
            if (_n > 0)
            {
                Array.Sort(Gaps, 0, _n);
                App.Logger?.Information(
                    "FrameProbe: {Fps:F1} fps, p95 {P95:F1} ms, max {Max:F1} ms, show {Show}, quiet {Quiet}, panel {Panel}",
                    _n * 1000.0 / (now - _windowStart), Gaps[(int)(_n * 0.95)], Gaps[_n - 1],
                    MotionFx.ShowRunning, MotionFx.PanelQuiet, App.MainWindowRef?.IsVisible == true);
            }
            _n = 0;
            _windowStart = now;
        }
    }
}
