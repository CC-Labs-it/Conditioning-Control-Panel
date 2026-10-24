using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The calibration bubble accuracy test's rules, moved out of the WPF WebcamCalibrationWindow
    /// (RunBubbleTestAsync / FitAxisTrim / MedianPoint) so both heads run the same copy. Four bubbles,
    /// one at a time; holding the gaze near one for ~0.7 s pops it. The dwell samples are ground truth
    /// ("the user is looking at the bubble"), so the per-bubble residuals feed a per-axis linear
    /// correction (offset AND scale) that the head applies as the calibration's GazeTrim.
    ///
    /// <para>Everything carries WIDER TOLERANCE VERTICALLY on purpose: vertical gaze error measures
    /// 1.5-2x horizontal (half the eyeball rotation per screen unit, plus the eyelid occluding the
    /// iris top and bottom). CHI 2025 gaze-target guidance: ~3 degrees wide by ~4 tall, so the visual
    /// bubble is 136 x 180 DIPs and the dwell zone is an ELLIPSE, not a circle.</para>
    /// </summary>
    public static class BubbleAccuracyTest
    {
        public const int TickMs = 33;
        public const double PopRadiusX = 130;       // gaze-to-center X half-extent that counts as "on it" (bubble half-width 68 + slack)
        public const double PopRadiusY = 195;       // 1.5x the X radius - matches the measured vertical:horizontal error ratio
        public const int DwellNeededMs = 700;
        public const int AvgWindowSamples = 12;     // ~400ms rolling mean drives the on-target decision
        public const int TimeoutMs = 10000;
        public const double NearMissMaxX = 250;     // timeout bubbles still yield a recal sample within this
        public const double NearMissMaxY = 375;     // 1.5x again - a 300 px vertical miss at the bottom row is a REAL sample
        public const double HeaderClear = 108;      // top strip owned by the "Accuracy test" caption; probe rows clamp below it
        public const int FreshGazeMs = 300;         // a gaze point older than this is not "on" anything
        private const double OuterFrac = 0.35;      // 0.5 -/+ 0.35 => the ideal 0.15 / 0.85 rows
        private const double InnerFrac = 0.12;      // 0.5 -/+ 0.12 => the ideal 0.38 / 0.62 rows

        /// <summary>
        /// The four probe centres in visit order. Rows are two MIRROR PAIRS about one common centre
        /// (left column = centre -/+ 0.35 h * scale, right column = centre -/+ 0.12 h * scale), so both
        /// columns keep the same mean height at any window size and vertical cross-talk cancels out of
        /// the x fit. When the ideal 0.15..0.85 span will not fit under the caption strip the centre
        /// moves first and both pairs compress by one shared factor only if that is not enough. Visit
        /// order alternates columns and puts the extreme rows 1st and 3rd (the bottom row, hardest,
        /// lands mid-test while concentration is still good).
        /// </summary>
        public static (double X, double Y)[] ProbeSpots(double w, double h, double ringHalfH)
        {
            double rowMin = HeaderClear + ringHalfH;
            double rowMax = h - ringHalfH - 16;
            double rowCenter, rowScale;
            bool degenerate = false;
            if (rowMin > rowMax)
            {
                // The caption strip plus one whole ring does not fit: all four probes land on one line.
                degenerate = true;
                rowCenter = h / 2.0;
                rowScale = 0;
                Log.Warning("[Webcam] bubble test cannot lay out probe rows at window height {H:F0} DIPs " +
                    "(needs at least {Need:F0}) - all four probes collapse to one line and the vertical fit is meaningless.",
                    h, rowMin + ringHalfH + 16);
            }
            else
            {
                double maxScale = (rowMax - rowMin) / (2.0 * OuterFrac * h);
                if (maxScale >= 1.0)
                {
                    // Keep the centre as close to h/2 as the band allows: the trim fit pivots there, which
                    // keeps its offset and slope estimates uncorrelated. Math.Clamp throws on min > max.
                    rowScale = 1.0;
                    double lo = rowMin + OuterFrac * h;
                    double hi = Math.Max(lo, rowMax - OuterFrac * h);
                    rowCenter = Math.Clamp(h / 2.0, lo, hi);
                }
                else
                {
                    rowScale = maxScale;
                    rowCenter = (rowMin + rowMax) / 2.0;
                }
            }

            // "Not already warned about", not "rowScale > 0": a zero-wide band yields scale 0 here too.
            if (!degenerate && rowScale < 0.999)
            {
                double spanFrac = 2.0 * OuterFrac * rowScale;
                if (spanFrac < 2.0 * 0.15)
                    Log.Warning("[Webcam] bubble-test probe span compressed to {Span:P0} of a {H:F0} DIP window " +
                        "(scale {Scale:F2}, centre {Centre:F0}) - under the 30% the trim fit needs for a vertical slope, " +
                        "so this run will be offset-only. Column balance is intact.", spanFrac, h, rowScale, rowCenter);
                else
                    Log.Information("[Webcam] bubble-test probe rows compressed to scale {Scale:F2} " +
                        "(span {Span:P0} of {H:F0} DIPs, centre {Centre:F0}). Column balance is intact.",
                        rowScale, spanFrac, h, rowCenter);
            }

            double outer = OuterFrac * h * rowScale, inner = InnerFrac * h * rowScale;
            return new[]
            {
                (w * 0.24, rowCenter - outer),   // top extreme
                (w * 0.76, rowCenter + inner),
                (w * 0.24, rowCenter + outer),   // bottom extreme (hardest)
                (w * 0.76, rowCenter - inner),
            };
        }

        /// <summary>
        /// One bubble's dwell: fed every <see cref="TickMs"/>. The on-target decision runs on the
        /// ROLLING MEAN of the (locked) gaze inside the dwell ellipse, so one wobble frame outside does
        /// not drop the target; leaving decays the dwell instead of resetting it. Residual math uses the
        /// pre-lock point so the lock-on never flatters the measurement.
        /// </summary>
        public sealed class Dwell
        {
            private readonly double _cx, _cy;
            private readonly List<(double X, double Y)> _inside = new();
            private readonly Queue<(long AtMs, (double X, double Y) P)> _recent = new();
            private readonly Queue<(double X, double Y)> _avg = new();

            public Dwell(double cx, double cy) { _cx = cx; _cy = cy; }

            // nowMs of the previous tick (0 = the bubble's start, as WPF's lastTick = started).
            private long _lastMs;

            public double DwellMs { get; private set; }
            public double Progress => DwellMs / DwellNeededMs;
            public bool Popped => DwellMs >= DwellNeededMs;

            /// <summary><paramref name="gaze"/> is the cursor (locked) point, null when not fresh;
            /// <paramref name="raw"/> the pre-lock point of the same frame.</summary>
            public void Tick(long nowMs, (double X, double Y)? gaze, (double X, double Y) raw)
            {
                if (gaze is { } g)
                {
                    _recent.Enqueue((nowMs, raw));
                    while (_recent.Count > 0 && nowMs - _recent.Peek().AtMs > 2000) _recent.Dequeue();
                    _avg.Enqueue(g);
                    while (_avg.Count > AvgWindowSamples) _avg.Dequeue();
                }

                double onTarget = double.MaxValue;
                if (gaze.HasValue && _avg.Count >= 3)
                {
                    double ex = (_avg.Average(q => q.X) - _cx) / PopRadiusX;
                    double ey = (_avg.Average(q => q.Y) - _cy) / PopRadiusY;
                    onTarget = Math.Sqrt(ex * ex + ey * ey);
                }
                // Real time since the last tick, not TickMs: Task.Delay(33) lands every ~47 ms or later,
                // so counting laps made the 700 ms dwell take ~1 s of the timeout (WPF a4a5aa43b).
                var stepMs = Services.Timing.TickStep.Ms(ref _lastMs, nowMs, Services.Timing.TickStep.DwellMaxStepMs);
                if (onTarget <= 1.0) { DwellMs += stepMs; _inside.Add(raw); }
                else DwellMs = Math.Max(0, DwellMs - stepMs);
            }

            /// <summary>The recal sample of a popped bubble: the median pre-lock point while on it.</summary>
            public (double X, double Y) PopSample => MedianPoint(_inside);

            /// <summary>A timed-out bubble still yields a sample when the gaze hovered consistently near it
            /// (the last 2 s, inside the near-miss ellipse): those are the biggest, most valuable errors.</summary>
            public (double X, double Y)? NearMissSample()
            {
                if (_recent.Count < 10) return null;
                var med = MedianPoint(_recent.Select(s => s.P).ToList());
                double nx = (med.X - _cx) / NearMissMaxX, ny = (med.Y - _cy) / NearMissMaxY;
                return nx * nx + ny * ny <= 1.0 ? med : null;
            }
        }

        public sealed record TrimFit(double X0, double X1, double Y0, double Y1, double RmsBefore, double RmsAfter, bool Apply);

        /// <summary>
        /// Per-axis linear correction (offset + centre-relative scale) through the (measured -> target)
        /// pairs, pivoting on the window centre; null below 2 samples. The scale terms are applied at
        /// half strength (damped approach across runs; keeps GazeTrim composing sanely). Apply only when
        /// it measurably helps (2 DIPs of rms) and is not noise-level.
        /// </summary>
        public static TrimFit? FitTrim(IReadOnlyList<((double X, double Y) Target, (double X, double Y) Measured)> samples, double w, double h)
        {
            if (samples.Count < 2) return null;
            double cx = w / 2.0, cy = h / 2.0;
            var (x0, x1) = FitAxisTrim(samples.Select(s => (s.Target.X, s.Measured.X)).ToList(), cx, w * 0.15);
            var (y0, y1) = FitAxisTrim(samples.Select(s => (s.Target.Y, s.Measured.Y)).ToList(), cy, h * 0.15);
            x1 *= 0.5;
            y1 *= 0.5;

            double before = 0, after = 0;
            foreach (var (t, m) in samples)
            {
                double rx = t.X - m.X, ry = t.Y - m.Y;
                before += rx * rx + ry * ry;
                double ax = m.X + x0 + x1 * (m.X - cx), ay = m.Y + y0 + y1 * (m.Y - cy);
                after += (t.X - ax) * (t.X - ax) + (t.Y - ay) * (t.Y - ay);
            }
            before = Math.Sqrt(before / samples.Count);
            after = Math.Sqrt(after / samples.Count);
            bool apply = after < before - 2
                && (Math.Abs(x0) >= 3 || Math.Abs(y0) >= 3 || Math.Abs(x1) >= 0.01 || Math.Abs(y1) >= 0.01);
            return new TrimFit(x0, x1, y0, y1, before, after, apply);
        }

        /// <summary>LS fit of residual = c0 + c1*(measured - center) over one axis. The slope needs 3+
        /// samples spanning <paramref name="minSpreadForScale"/>; with less it degrades to offset-only.
        /// Clamps keep one bad test from warping the map (the composed total is clamped again).</summary>
        public static (double C0, double C1) FitAxisTrim(IReadOnlyList<(double Target, double Measured)> pts, double center, double minSpreadForScale)
        {
            int n = pts.Count;
            double meanU = 0, meanR = 0, minU = double.MaxValue, maxU = double.MinValue;
            foreach (var (t, m) in pts)
            {
                double u = m - center;
                meanU += u; meanR += t - m;
                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
            }
            meanU /= n; meanR /= n;

            double c1 = 0;
            if (n >= 3 && maxU - minU >= minSpreadForScale)
            {
                double varU = 0, cov = 0;
                foreach (var (t, m) in pts)
                {
                    double du = m - center - meanU, dr = (t - m) - meanR;
                    varU += du * du;
                    cov += du * dr;
                }
                if (varU > 1e-6) c1 = Math.Clamp(cov / varU, -0.25, 0.25);
            }
            return (Math.Clamp(meanR - c1 * meanU, -250, 250), c1);
        }

        /// <summary>The verify panel's status line after a run (WPF text).</summary>
        public static string Summary(int popped, int total, IReadOnlyList<double> errors, TrimFit? fit)
        {
            bool tuned = fit?.Apply == true;
            if (popped == 0 && !tuned)
                return "Couldn't pop any bubbles — tracking may be off for this setup. Consider recalibrating.";
            var text = popped > 0
                ? $"Popped {popped} / {total} · average accuracy {errors.Average():F0} px"
                : $"Popped 0 / {total}";
            return tuned
                ? text + $" · recalibrated from your aim ({fit!.RmsBefore:F0}→{fit.RmsAfter:F0} px). Run it again to verify!"
                : text + ".";
        }

        public static (double X, double Y) MedianPoint(IReadOnlyList<(double X, double Y)> pts)
        {
            var xs = pts.Select(p => p.X).OrderBy(v => v).ToList();
            var ys = pts.Select(p => p.Y).OrderBy(v => v).ToList();
            return (xs[xs.Count / 2], ys[ys.Count / 2]);
        }
    }
}
