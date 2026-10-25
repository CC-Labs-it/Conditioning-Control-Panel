using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ConditioningControlPanel.Avalonia.Controls;
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
    /// <para>Replay uses host fullscreen and ducks/restores the panel. Chromium no-throttle flags
    /// have no WebKitGTK equivalent.</para>
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

        // Reuse the games panic registry and single-window rule; replay does not require an open shop door.
        internal static GameWindow? LaunchJustDropReplay(string code, Window? main = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            GameWindow? live;
            lock (Open) live = Open.Find(w => w.Spec.Id == JustDropId);
            if (live != null) { live.Activate(); return live; }
            var window = new GameWindow(Games[JustDropId]);
            window.LoadJustDrop(JustDropHandoff.ReplayPath(code));
            window.WindowState = WindowState.FullScreen;
            var before = main?.WindowState ?? WindowState.Normal;
            bool duck = main is { IsVisible: true } && before != WindowState.Minimized;
            window.Closed += (_, _) =>
            {
                if (duck && main is { IsVisible: true, WindowState: WindowState.Minimized })
                {
                    main.WindowState = before == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                    main.Activate();
                }
            };
            window.Show();
            if (duck) { main!.WindowState = WindowState.Minimized; window.Activate(); }
            return window;
        }

        private void InstallJustDropControls()
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            Content = null;
            Grid.SetRow(Web, 1);
            grid.Children.Add(Web);
            var toggle = new Button
            {
                Content = new IconGlyph { Kind = IconKind.FullScreenMaximize, Size = 18 },
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            };
            toggle.Click += (_, _) => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            ToolTip.SetTip(toggle, "Fullscreen / Esc");
            // ponytail: separate native-web row keeps the host toggle above native browser content.
            grid.Children.Add(toggle);
            Content = grid;
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                WindowState = WindowState.Normal;
                e.Handled = true;
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
        private void LoadJustDrop(string? next = null)
        {
            PageUrl = new Uri(JustDropHandoff.BuildStartUrl(next ?? JustDropHandoff.ShopPath, JustDropHandoff.UnifiedId(), JustDropHandoff.AuthToken()));
            Web.AllowNavigation = JustDropHandoff.IsSite;
            Web.RequestHeader = uri => JustDropHeaderFor(uri, JustDropHandoff.AuthToken());
            Web.InjectCarrier = true;   // the remote page speaks chrome.webview; WebKitGTK needs the shim
            InstallJustDropControls();
            Web.Navigate(PageUrl);
            Closed += (_, _) => JustDropOrdersService.NoteDrawerChanged();
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
