using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The pure maths behind WPF VideoService's memory-render (blurred-background) surface: the
    /// clip's display aspect, the letterbox rect, the staging buffer size, the blur-fill snapshot
    /// and the format-generation stamp. The blur-fill decision itself is
    /// <see cref="MandatoryVideoScheduler.NeedsBlurFill"/>.
    /// </summary>
    internal static class VideoRenderMath
    {
        /// <summary>
        /// True DISPLAY aspect (w/h) of a video track: the coded frame size corrected by the sample
        /// aspect ratio, and transposed when the track carries a 90°/270° rotation (LibVLC rotates
        /// the vmem buffer for us via video_format_ApplyRotation, but the track still reports the
        /// pre-rotation size). Returns 0 when the numbers can't describe a real frame.
        ///
        /// #786: the vmem format callback hands us the CODED size with the SAR already dropped, so
        /// an anamorphic clip (non-square pixels) arrived as, say, 640x368 when its real display
        /// shape is much taller — and rendering that buffer at its own pixel aspect stretched the
        /// picture sideways until it filled the screen. Never derive the on-screen shape from the
        /// buffer when the track can tell us the truth. Pure — unit-tested.
        /// </summary>
        internal static double DisplayAspectFrom(uint width, uint height, uint sarNum, uint sarDen, bool transposed)
        {
            if (width == 0 || height == 0) return 0;
            double num = width * (sarNum > 0 ? sarNum : 1u);
            double den = height * (sarDen > 0 ? sarDen : 1u);
            if (den <= 0) return 0;
            double aspect = num / den;
            if (transposed) aspect = 1.0 / aspect;
            if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0) return 0;
            // Sanity fence: real clips live well inside 16:1 .. 1:16. Anything outside is a garbage
            // SAR and must fall back to the buffer aspect rather than produce a sliver of video.
            if (aspect > 16.0 || aspect < 1.0 / 16.0) return 0;
            return aspect;
        }

        /// <summary>
        /// Largest rect of <paramref name="aspect"/> (w/h) that fits inside the container —
        /// i.e. letterbox or pillarbox, never stretch. Returns (0,0) when there is nothing sane to
        /// fit yet. Pure — unit-tested.
        /// </summary>
        internal static (double W, double H) FitToAspect(double containerW, double containerH, double aspect)
        {
            if (containerW <= 0 || containerH <= 0 || aspect <= 0 ||
                double.IsNaN(aspect) || double.IsInfinity(aspect)) return (0, 0);

            double w = containerW;
            double h = containerW / aspect;
            if (h > containerH)
            {
                h = containerH;
                w = containerH * aspect;
            }
            return (w, h);
        }

        /// <summary>
        /// Byte size of one BGRA frame of the given geometry, or 0 if the geometry is absurd
        /// (overflow / zero side). A bogus format callback must make the blit SKIP the frame, never
        /// allocate or copy on a garbage length. Pure — unit-tested.
        /// </summary>
        internal static int StagingBytesFor(uint w, uint h)
        {
            if (w == 0 || h == 0) return 0;
            if (w > 65535 || h > 65535) return 0; // no real decoded frame is this big; keeps the product in range
            long bytes = (long)w * h * 4;
            if (bytes > int.MaxValue) return 0;
            return (int)bytes;
        }

        /// <summary>
        /// Geometry of the small snapshot that feeds the blurred fill: the frame divided down, floored
        /// at 8px a side so a tiny clip can't degenerate to a 0-wide bitmap. Pure — unit-tested.
        /// </summary>
        internal static (int W, int H) ComputeSnapshotDims((uint W, uint H) buffer, int divisor)
        {
            int d = Math.Max(1, divisor);
            int sw = Math.Max(8, (int)buffer.W / d);
            int sh = Math.Max(8, (int)buffer.H / d);
            return (sw, sh);
        }

        /// <summary>
        /// Whether this frame index should refresh the blurred fill's snapshot. The fill sits behind a
        /// radius-48 Gaussian, so it does not need per-frame freshness. Pure — unit-tested.
        /// </summary>
        internal static bool IsSnapshotFrame(int frameIndex, int everyN)
            => everyN <= 1 || (frameIndex % everyN) == 0;

        /// <summary>
        /// Whether a bitmap rebuild posted with <paramref name="postedGeneration"/> is still the newest
        /// one (#687: three format callbacks landed within 15ms and an older rebuild ran last, pinning
        /// the bitmap at a size that no longer matched the buffer). Pure — unit-tested.
        /// </summary>
        internal static bool IsRebuildCurrent(int postedGeneration, int currentGeneration)
            => postedGeneration == currentGeneration;
    }
}
