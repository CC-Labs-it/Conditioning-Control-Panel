using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// Which <see cref="PerformanceTier"/> is in effect (WPF PerformanceProfile.CurrentTier, moved
    /// here so both heads share one copy). <see cref="AppSettings.PerformanceMode"/> forces
    /// Performance; else <see cref="AppSettings.AutoPerformanceMode"/> escalates by the live count of
    /// heavy on-screen elements (flash windows + ambient bubbles), which each head counts itself;
    /// else Quality.
    /// </summary>
    public static class PerformanceTierRule
    {
        public const int BalancedThreshold = 8;
        public const int PerformanceThreshold = 16;

        public static PerformanceTier Resolve(bool performanceMode, bool autoPerformanceMode, int heavyElements)
        {
            if (performanceMode) return PerformanceTier.Performance;
            if (!autoPerformanceMode) return PerformanceTier.Quality;
            if (heavyElements >= PerformanceThreshold) return PerformanceTier.Performance;
            if (heavyElements >= BalancedThreshold) return PerformanceTier.Balanced;
            return PerformanceTier.Quality;
        }
    }
}
