using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>Bounded residual folding used by click-driven gaze correction.</summary>
    public static class GazeDriftCorrection
    {
        /// <summary>
        /// Fold a click-to-fixation residual into the existing offset. Fast progress is the
        /// elapsed fraction of a 45 second adaptation window, or null for normal gain.
        /// Returns null when the residual is too small or too large to use.
        /// </summary>
        public static (double Dx, double Dy)? Fold(double residualX, double residualY,
            double previousX, double previousY, double? fastProgress = null)
        {
            double residual = Math.Sqrt(residualX * residualX + residualY * residualY);
            double ramp = fastProgress.HasValue ? 1 - Math.Clamp(fastProgress.Value, 0, 1) : 0;
            double cap = 220 + 80 * ramp;
            if (!double.IsFinite(residual) || residual < 15 || residual > cap) return null;
            double gain = 0.15 + 0.30 * ramp;
            return (Math.Clamp(previousX + residualX * gain, -300, 300),
                    Math.Clamp(previousY + residualY * gain, -300, 300));
        }
    }
}
