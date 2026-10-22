namespace ConditioningControlPanel.Services.Browser;

internal static class RemoteBrowserStopRule
{
    // WebView2 paints above the WPF remote card unless its host is hidden.
    internal static bool HideBrowser(bool overlayVisible, bool remoteVideoActive)
        => overlayVisible && !remoteVideoActive;
}
