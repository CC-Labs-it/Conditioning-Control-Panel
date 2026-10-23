namespace ConditioningControlPanel.Helpers;

/// <summary>
/// The pure decision behind both heads' AdaptiveSideArt (WPF Features/AdaptiveSideArt.cs and
/// CCP.Avalonia Helpers/AdaptiveSideArt.cs): when a feature page's side-art column folds away.
/// </summary>
public static class SideArtCollapse
{
    /// <summary>
    /// How far past the threshold a collapsed page has to grow before the art comes back. The
    /// switch changes the page's height, which shows or hides the host ScrollViewer's vertical
    /// scrollbar, which changes the Grid's width by that scrollbar's width: with one threshold
    /// both ways a page sitting within a scrollbar of it flipped between wide and compact on
    /// every layout pass (ccp-bugs #1321, Awareness and Listening). Wider than any scrollbar.
    /// </summary>
    public const double WidenMargin = 48;

    /// <summary>
    /// Collapse below <paramref name="threshold"/>, and once collapsed stay collapsed until the
    /// width reaches the threshold plus <see cref="WidenMargin"/>.
    /// </summary>
    public static bool ShouldCollapse(double width, double threshold, bool collapsedNow)
        => collapsedNow ? width < threshold + WidenMargin : width < threshold;
}
