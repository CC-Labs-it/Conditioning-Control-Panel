using System;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// The read-only "how busy is this file" strip on Play > Deeper (tier 2 testers,
    /// 2026-11-16: "keep the effect heat map too but I surely don't need to edit anything").
    /// Pure on purpose (no WPF), so the buckets are pinned by unit tests and the strip
    /// control only paints what this returns.
    ///
    /// <para>What counts: every timeline EFFECT (flash, bubble, subliminal, overlay, speak,
    /// haptic) and every haptic-track event. Reactive rules (gaze, blink, region) have no
    /// fixed time, so they are not on the map. Regions are labels, not effects.</para>
    /// </summary>
    public static class EffectHeatMap
    {
        /// <summary>A point effect (no duration) still lights the bucket it lands in.</summary>
        private const double PointWeight = 1.0;

        /// <summary>
        /// The length the map spreads over: the media length when known, else the file's own
        /// stored length, else the end of the last effect. 0 when nothing says anything.
        /// </summary>
        public static double ResolveDuration(Enhancement? enh, double knownSeconds = 0)
        {
            if (knownSeconds > 0 && !double.IsNaN(knownSeconds) && !double.IsInfinity(knownSeconds))
                return knownSeconds;
            if (enh == null) return 0;
            var stored = enh.Metadata?.MediaDurationSeconds ?? 0;
            if (stored > 0 && !double.IsInfinity(stored)) return stored;
            double end = 0;
            foreach (var it in enh.TimelineItems ?? new())
                if (it != null && it.Kind == TimelineItemKind.Effect)
                    end = Math.Max(end, it.Start + SpanOf(it));
            foreach (var track in enh.HapticTracks ?? new())
                foreach (var ev in track?.Events ?? new())
                    if (ev != null) end = Math.Max(end, ev.Start + Math.Max(0, ev.Duration));
            return end;
        }

        /// <summary>
        /// <paramref name="buckets"/> values in 0..1 (1 = the busiest stretch of the file).
        /// All zeros for an empty file, no length, or no buckets.
        /// </summary>
        public static double[] Compute(Enhancement? enh, double durationSeconds, int buckets)
        {
            if (buckets <= 0) return Array.Empty<double>();
            var heat = new double[buckets];
            if (enh == null || !(durationSeconds > 0) || double.IsInfinity(durationSeconds)) return heat;

            double bucketLen = durationSeconds / buckets;
            foreach (var it in enh.TimelineItems ?? new())
            {
                if (it == null || it.Kind != TimelineItemKind.Effect) continue;
                Add(heat, bucketLen, it.Start, SpanOf(it), Weight(it.EffectIntensity));
            }
            foreach (var track in enh.HapticTracks ?? new())
                foreach (var ev in track?.Events ?? new())
                    if (ev != null) Add(heat, bucketLen, ev.Start, Math.Max(0, ev.Duration), Weight(ev.Intensity));

            double max = 0;
            foreach (var h in heat) max = Math.Max(max, h);
            if (max <= 0) return heat;
            for (int i = 0; i < heat.Length; i++) heat[i] /= max;
            return heat;
        }

        /// <summary>How long an effect occupies the timeline: its block length, else how long it plays.</summary>
        private static double SpanOf(TimelineItem it)
        {
            if (it.Duration > 0) return it.Duration;
            return Math.Max(0, it.EffectDurationMs) / 1000.0;
        }

        /// <summary>A faint effect still shows; intensity only shades it (0.25..1).</summary>
        private static double Weight(double intensity)
        {
            if (double.IsNaN(intensity)) return 1.0;
            return Math.Clamp(intensity, 0.25, 1.0);
        }

        private static void Add(double[] heat, double bucketLen, double start, double span, double weight)
        {
            if (double.IsNaN(start) || double.IsNaN(span)) return;
            start = Math.Max(0, start);
            if (span <= 0)
            {
                int i = (int)Math.Floor(start / bucketLen);
                if (i >= heat.Length) return;
                heat[i] += PointWeight * weight;
                return;
            }
            double end = start + span;
            int first = (int)Math.Floor(start / bucketLen);
            int last = Math.Min(heat.Length - 1, (int)Math.Floor(end / bucketLen));
            for (int i = first; i <= last && i < heat.Length; i++)
            {
                double b0 = i * bucketLen, b1 = b0 + bucketLen;
                double overlap = Math.Min(end, b1) - Math.Max(start, b0);
                // Weight by the share of the bucket the effect covers, so a long overlay
                // reads as a steady glow and a burst of short flashes as a hot spot.
                if (overlap > 0) heat[i] += weight * (overlap / bucketLen);
            }
        }
    }
}
