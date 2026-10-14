namespace ConditioningControlPanel.Services.UI;

/// <summary>WPF AdaptiveSideArt's resize hysteresis: avoid flipping when a scrollbar changes width.</summary>
public static class SideArtRules
{
    public const double WidenMargin = 48;

    public static bool ShouldCollapse(double width, double threshold, bool collapsedNow)
        => collapsedNow ? width < threshold + WidenMargin : width < threshold;
}
