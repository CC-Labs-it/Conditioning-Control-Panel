using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The portable half of an animated flash decode: WPF FlashService.LoadGifFrames ->
    /// AnimatedWebp.DecodeFrames (maxFrames 60, 30 MB) under ComputeDecodeMaxDim's 2048 px cap.
    /// The spiral keeps its own <see cref="SpiralFrames"/> rules; this is the flash's.
    /// </summary>
    public static class FlashFrames
    {
        /// <summary>WPF FlashService.cs:1494 ComputeDecodeMaxDim: never decoded above 2048 px.</summary>
        public const int MaxDecodeDimension = 2048;
        /// <summary>WPF FlashService.cs:1404,1432 (maxFrames: 60, maxMemoryMb: 30.0).</summary>
        public const int MaxFrames = 60;
        public const double MaxMemoryMb = 30.0;
        /// <summary>WPF AnimatedWebp.cs:89 DECODE_CEILING: frames past this are never composed.</summary>
        public const int DecodeCeiling = 600;

        /// <summary>
        /// The decode size (long side at most the display long side and 2048, never upscaled) and
        /// the frames kept: WPF AnimatedWebp.cs:182-188, at least 10 when the 30 MB budget bites,
        /// at most 60, a ceiling stride across the whole clip (#683), and only the first 600
        /// frames decoded.
        /// </summary>
        public static (int Width, int Height, int Keep, int Step, int DecodeCount) Plan(
            int width, int height, int frameCount, int displayLongSide)
        {
            var maxDim = displayLongSide > 0 ? Math.Min(displayLongSide, MaxDecodeDimension) : MaxDecodeDimension;
            int longest = Math.Max(1, Math.Max(width, height));
            int w = Math.Max(1, width), h = Math.Max(1, height);
            if (longest > maxDim)
            {
                var scale = maxDim / (double)longest;
                (w, h) = (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
            }
            frameCount = Math.Max(1, frameCount);
            var estimatedMb = (long)w * h * 4 * frameCount / (1024.0 * 1024.0);
            int keep = frameCount;
            if (estimatedMb > MaxMemoryMb)
                keep = Math.Max(10, (int)(frameCount * (MaxMemoryMb / estimatedMb)));
            keep = Math.Max(1, Math.Min(keep, MaxFrames));
            int step = Math.Max(1, (int)Math.Ceiling(frameCount / (double)keep));
            return (w, h, keep, step, Math.Min(frameCount, DecodeCeiling));
        }

        /// <summary>
        /// WPF AnimatedWebp.cs:242-244: the average delay of the kept frames that carry one
        /// (none -> 100 ms, under 20 ms -> 100 ms), times the stride, held to 20..2000 ms.
        /// </summary>
        public static double DelayMs(long keptDurationTotalMs, int keptDurationSamples, int step)
        {
            double avgMs = keptDurationSamples > 0 ? (double)keptDurationTotalMs / keptDurationSamples : 100;
            if (avgMs < 20) avgMs = 100;
            return Math.Clamp(avgMs * Math.Max(1, step), 20, 2000);
        }
    }
}
