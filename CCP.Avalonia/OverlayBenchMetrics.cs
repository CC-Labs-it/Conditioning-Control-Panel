using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>The pure half of <see cref="OverlayBench"/>: percentiles, missed-frame counting and
    /// the pass/fail verdict, kept apart so CI can prove the thresholds without a display.
    /// Thresholds are the compositor decision's gate (docs/avalonia-decisions.md, 2026-10-22).</summary>
    internal static class OverlayBenchMetrics
    {
        /// <summary>Max UI-thread latency a pass allows.</summary>
        public const double MaxUiLatencyMs = 100;
        /// <summary>A UI-thread latency or a window frame gap at or above this is a stall (the
        /// ~1.7 s GLX/XWayland flash stall in the parity doc is the shape this catches).</summary>
        public const double StallMs = 1000;
        /// <summary>Nominal frame interval (60 Hz) that missed frames are counted against.</summary>
        public const double FrameMs = 1000.0 / 60;

        /// <summary>Nearest-rank percentile; 0 for no samples.</summary>
        public static double Percentile(IReadOnlyList<double> samples, double p)
        {
            if (samples.Count == 0) return 0;
            var sorted = samples.OrderBy(x => x).ToArray();
            var rank = (int)Math.Ceiling(p / 100.0 * sorted.Length);
            return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
        }

        /// <summary>Frames a gap of <paramref name="gapMs"/> skipped: one interval is on time, two
        /// intervals is one missed frame.</summary>
        public static int Missed(double gapMs) => Math.Max(0, (int)Math.Round(gapMs / FrameMs) - 1);

        /// <summary>Every reason the run fails; empty means pass. A run that measured nothing fails
        /// too, so a broken bench cannot report green.</summary>
        public static List<string> Failures(IReadOnlyList<double> uiLatencyMs, IEnumerable<FrameTrack> windows)
        {
            var fails = new List<string>();
            var tracks = windows.ToList();
            if (uiLatencyMs.Count == 0) fails.Add("no UI-thread latency samples");
            if (tracks.All(t => t.Frames < 2)) fails.Add("no overlay window drew two frames");
            var max = uiLatencyMs.Count == 0 ? 0 : uiLatencyMs.Max();
            if (max > MaxUiLatencyMs) fails.Add($"max UI latency {max:0.0} ms > {MaxUiLatencyMs} ms");
            if (max >= StallMs) fails.Add($"UI-thread stall {max:0} ms");
            foreach (var t in tracks.Where(t => t.MaxGapMs >= StallMs))
                fails.Add($"frame stall {t.MaxGapMs:0} ms in {t.Name}");
            return fails;
        }
    }

    /// <summary>Frame timestamps of one overlay window.</summary>
    internal sealed class FrameTrack
    {
        private double? _last;
        public FrameTrack(string name) => Name = name;
        public string Name { get; }
        public int Frames { get; private set; }
        public int Missed { get; private set; }
        public double MaxGapMs { get; private set; }

        public void Add(double nowMs)
        {
            Frames++;
            if (_last is { } last)
            {
                var gap = nowMs - last;
                Missed += OverlayBenchMetrics.Missed(gap);
                MaxGapMs = Math.Max(MaxGapMs, gap);
            }
            _last = nowMs;
        }
    }
}
