using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
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
    /// <para>Takeaway replay uses the bare player, fullscreen and panel duck/restore. Remaining:
    /// Chromium no-throttle flags (WebKitGTK has no equivalent).</para>
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
            PageUrl = new Uri(JustDropHandoff.BuildStartUrl(Spec.Query ?? JustDropHandoff.ShopPath, JustDropHandoff.UnifiedId(), JustDropHandoff.AuthToken()));
            Web.AllowNavigation = JustDropHandoff.IsSite;
            Web.RequestHeader = uri => JustDropHeaderFor(uri, JustDropHandoff.AuthToken());
            Web.InjectCarrier = true;   // the remote page speaks chrome.webview; WebKitGTK needs the shim
            // WPF ChaosWebViewHost.cs:1227-1231,1562-1578: Escape while the page owns focus.
            Web.NavigationCompleted += uri =>
            {
                if (JustDropHandoff.IsSite(uri)) _ = Web.InvokeScriptAsync(
                    "(function(){if(window.__ccpHostEscape)return;window.__ccpHostEscape=true;" +
                    "window.addEventListener('keydown',function(e){if(e.key!=='Escape'&&e.keyCode!==27)return;" +
                    "try{window.chrome.webview.postMessage({type:'ccp-host-escape'});}catch(_){}},true);})();");
            };
            BuildJustDropChrome();
            Web.Navigate(PageUrl);
            Log.Information("[Game] justdrop: launched (shop)");   // never the url: it names the account
        }

        // WPF Chaos/ChaosWebViewHost.cs:620-678: a permanent title and host-owned toggle,
        // outside the remote page. No shop or order UI is recreated here.
        private void BuildJustDropChrome()
        {
            Content = null; // detach the native web host before placing it under the strip
            var toggle = new Button
            {
                Name = "JustDropFullscreen", MinWidth = 120,
                Theme = this.TryFindResource("SmallPinkButton", out var theme) ? theme as ControlTheme : null,
                Margin = new Thickness(8, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center
            };
            void Stamp() => toggle.Content = Loc.Get(WindowState == WindowState.FullScreen
                ? "btn_exit_fullscreen" : "btn_fullscreen");
            Stamp();
            ToolTip.SetTip(toggle, Loc.Get("tooltip_fill_the_screen_esc_brings_the_window_back"));
            toggle.Click += (_, _) => WindowState = WindowState == WindowState.FullScreen
                ? WindowState.Normal : WindowState.FullScreen;
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) Stamp(); };
            var bar = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(toggle, Dock.Right);
            bar.Children.Add(toggle);
            bar.Children.Add(new TextBlock
            {
                Text = "Just Drop", Margin = new Thickness(12, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
                FontWeight = FontWeight.SemiBold, Foreground = this.TryFindResource("PinkBrush", out var pink) ? pink as IBrush : Brushes.HotPink
            });
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(new Border
            {
                Background = this.TryFindResource("PanelBgBrush", out var bg) ? bg as IBrush : Brushes.Black,
                BorderBrush = this.TryFindResource("TransparentPink40Brush", out var border) ? border as IBrush : Brushes.HotPink,
                BorderThickness = new Thickness(0, 0, 0, 1), Child = bar
            });
            Grid.SetRow(Web, 1);
            grid.Children.Add(Web);
            Content = grid;
            // WPF :685-693: Escape from host chrome exits fullscreen before a child consumes it.
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape || WindowState != WindowState.FullScreen) return;
                WindowState = WindowState.Normal;
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        internal static GameWindow? LaunchJustDropReplay(string code, Windows.MainShellWindow shell)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            lock (Open)
            {
                var live = Open.Find(w => w.Spec.Id == JustDropId);
                if (live is not null) { live.Activate(); return live; }
            }
            var window = new GameWindow(Games[JustDropId] with { Query = JustDropHandoff.ReplayPath(code) });
            window.LoadJustDrop();
            var state = shell.WindowState;
            bool duck = shell.IsVisible && state != global::Avalonia.Controls.WindowState.Minimized;
            window.WindowState = global::Avalonia.Controls.WindowState.FullScreen;
            window.Closed += (_, _) =>
            {
                if (duck && shell.IsVisible && shell.WindowState == global::Avalonia.Controls.WindowState.Minimized)
                    shell.WindowState = state;
            };
            window.Show();
            if (duck) { shell.WindowState = global::Avalonia.Controls.WindowState.Minimized; window.Activate(); }
            return window;
        }

        /// <summary>WPF OnPageMessage (:227): the raw envelope to Core, so the source/version checks stay in one place.</summary>
        private void HandleJustDrop(JObject o)
        {
            if ((string?)o["type"] == "ccp-host-escape")
            {
                if (WindowState == WindowState.FullScreen) WindowState = WindowState.Normal;
                return;
            }
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { Log.Warning(ex, "[Game] justdrop: message handling failed"); }
        }
    }
}
