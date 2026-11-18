namespace ConditioningControlPanel.Services.Browser;

internal static class RemoteBrowserStopRule
{
    // WebView2 paints above the WPF remote card unless its host is hidden.
    internal static bool HideBrowser(bool overlayVisible, bool remoteVideoActive)
        => overlayVisible && !remoteVideoActive;

    // A fullscreen enter under the remote card is refused when it is a late event from a page
    // that was stopped. A takeover the app just started (the controller switched Autonomy on and
    // it picked a web video) is not that: its script is armed, and a stop clears the arm.
    internal static bool RefuseFullscreen(bool overlayVisible, bool remoteVideoActive, bool takeoverArmed)
        => HideBrowser(overlayVisible, remoteVideoActive) && !takeoverArmed;
}
