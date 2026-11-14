using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// One rule for handing the native browser back (Nov 4 review, #4384). The remote overlay, the
    /// leash gate and both tutorial overlays each hide the browser card while they are up, because a
    /// native web view paints over everything drawn in the window (WPF's WebView2 airspace). Before
    /// this, each one put the browser back by itself when it went, so a controller leaving brought
    /// the browser back over a leash gate's Panic and Cut, or over a running tour. Now every surface
    /// gives the browser back through <see cref="RestoreBrowser"/>, which leaves it hidden while any
    /// other cover is still up.
    /// </summary>
    public partial class MainShellWindow
    {
        /// <summary>True while any surface that needs the browser out of the way is still up. The remote overlay stops
        /// covering while the controller's own video plays (WPF RevealBrowserForRemoteVideo).</summary>
        internal bool BrowserCovered =>
            (Named<Border>("RemoteControlOverlay")?.IsVisible == true && !_remoteOverlayBrowserRevealed)
            || _leashGate?.IsUp == true
            || Named<Control>("MainTutorialOverlay")?.IsVisible == true
            || Tours.TutorialHead.ShellOverlay != null;

        /// <summary>A cover went away: show the browser only when no other cover is still up. A leash
        /// gate that is still up takes over the hand-back, so the browser returns when the gate goes.</summary>
        internal void RestoreBrowser()
        {
            if (LeashBrowser is not { } browser) return;
            if (!BrowserCovered)
            {
                browser.IsVisible = true;
                return;
            }
            if (_leashGate?.IsUp == true) _leashHidBrowser = true;
        }
    }
}
