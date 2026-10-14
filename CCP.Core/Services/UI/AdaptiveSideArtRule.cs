namespace ConditioningControlPanel.Services.UI
{
    /// <summary>Feature-page art collapses below its threshold and returns only after a
    /// scrollbar-sized layout change can no longer flip it back (WPF bug #1321).</summary>
    public static class AdaptiveSideArtRule
    {
        public const double WidenMargin = 48;

        public static bool ShouldCollapse(double width, double threshold, bool collapsedNow)
            => collapsedNow ? width < threshold + WidenMargin : width < threshold;
    }
}
