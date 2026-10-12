using System;
using ConditioningControlPanel.Services.JustDrop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// JUST DROP's window (WPF Services/JustDrop/JustDropHostService.cs): the live shop at
    /// app.cclabs.app in a window of its own, signed in by the handoff (Core <see cref="JustDropHandoff"/>).
    /// The only gate is the server's door flag, re-checked on every open (WPF ShowTab, MainWindow.TabNavigation.cs:264).
    /// Nothing is served from disk; navigation stays on the site's https origin (WPF PrimaryHost); every
    /// page message goes to Core <see cref="JustDropService"/>; a panic closes it with every other game
    /// window (PanicSurfaces "games", WPF GameSurfaces.cs:57).
    /// <para>Not here: WPF LaunchReplay's fullscreen + duck (no Takeaway shelf caller on this head yet),
    /// HostOwnedFullscreen's strip, and the Chromium no-throttle flags (WebKitGTK has no equivalent).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string JustDropId = "justdrop";

        /// <summary>WPF ShowTab("justdrop"): a door the account cannot see is a no-op, never a redirect.</summary>
        private static bool JustDropDoorOpen()
        {
            if (JustDropService.DoorAvailable) return true;
            Log.Debug("[Game] justdrop ignored: the door is not available on this account");
            return false;
        }

        /// <summary>WPF AttachAuthHeader: the token rides the handoff request once in this window's life,
        /// never any other url (WPF filters to Document requests; this engine names no resource kind).
        /// Spent only once the engine took the header, so a refused set leaves the next try signed in.</summary>
        internal (string Name, string Value)? JustDropHeaderFor(Uri uri, string? authToken)
        {
            if (Web.StampedRequests > 0 || JustDropHandoff.AuthHeaderFor(uri, authToken) is not { } token) return null;
            return (JustDropHandoff.AuthHeaderName, token);
        }

        /// <summary>WPF Launch(ShopPath) (:75-158): windowed, the panel stays where it is (this is browsing).</summary>
        private void LoadJustDrop()
        {
            PageUrl = new Uri(JustDropHandoff.BuildStartUrl(JustDropHandoff.ShopPath, JustDropHandoff.UnifiedId(), JustDropHandoff.AuthToken()));
            Web.AllowNavigation = JustDropHandoff.IsSite;
            Web.RequestHeader = uri => JustDropHeaderFor(uri, JustDropHandoff.AuthToken());
            Web.InjectCarrier = true;   // the remote page speaks chrome.webview; WebKitGTK needs the shim
            Web.Navigate(PageUrl);
            Log.Information("[Game] justdrop: launched (shop)");   // never the url: it names the account
        }

        /// <summary>WPF OnPageMessage (:227): the raw envelope to Core, so the source/version checks stay in one place.</summary>
        private void HandleJustDrop(JObject o)
        {
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { Log.Warning(ex, "[Game] justdrop: message handling failed"); }
        }
    }
}
