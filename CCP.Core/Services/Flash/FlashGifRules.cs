using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
namespace ConditioningControlPanel.Services.Flash;
internal static class FlashGifRules
{
    internal const double MIN_GIF_FRAME_DELAY_MS = 10.0;
    internal static TimeSpan ScaleFrameDelay(TimeSpan sourceDelay, double multiplier)
    {
        var ms = sourceDelay.TotalMilliseconds;
        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0) ms = 100.0;
        if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0) multiplier = 1.0;
        multiplier = Math.Clamp(multiplier, 0.25, 4.0);
        return TimeSpan.FromMilliseconds(Math.Max(MIN_GIF_FRAME_DELAY_MS, ms / multiplier));
    }
    internal static int MaxDecodeDimension(PerformanceTier tier) => tier switch
    {
        PerformanceTier.Performance => 512,
        PerformanceTier.Balanced => 768,
        _ => 1024,
    };
    internal static int DecodeMax(PerformanceTier tier, int scale) =>
        Math.Clamp((int)(MaxDecodeDimension(tier) * (scale / 100.0)), 256, 2048);
    internal const int DECODE_CEILING = 600;
    internal readonly record struct FramePlan(int Step, int DecodeCount, int MaxKeep)
    {
        public static FramePlan Create(int frameCount, int maxKeep, int decodeCeiling = DECODE_CEILING)
        {
            int keep = Math.Max(1, maxKeep);
            int step = Math.Max(1, (int)Math.Ceiling(frameCount / (double)keep));
            return new FramePlan(step, Math.Clamp(frameCount, 0, Math.Max(0, decodeCeiling)), keep);
        }
        public IEnumerable<int> KeptIndices()
        {
            int kept = 0;
            for (int i = 0; i < DecodeCount && kept < MaxKeep; i++)
                if (i % Step == 0) { kept++; yield return i; }
        }
    }
}
