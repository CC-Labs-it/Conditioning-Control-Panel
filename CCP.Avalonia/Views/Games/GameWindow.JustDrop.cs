using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    /// Page fullscreen confinement, native ownership and Chromium no-throttle flags remain missing.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string JustDropId = "justdrop";

        // WPF ChaosWebViewHost.cs:1222-1231, :1567-1572: hear Escape in the focused page.
        // The flag makes the completed-navigation fallback safe after a native install.
        internal const string JustDropEscapeScript = "(function(){try{if(window.__ccpHostEscape)return;"
            + "window.__ccpHostEscape=true;window.addEventListener('keydown',function(e){"
            + "if(e.key!=='Escape'&&e.keyCode!==27)return;"
            + "try{window.chrome.webview.postMessage({type:'ccp-host-escape'});}catch(_){}"
            + "},true);}catch(_){}})();";

        private Button? _justDropFullscreen;
        private WindowState _justDropPreviousState = WindowState.Normal;

        // WPF ChaosWebViewHost.cs:257-269, :620-653: the strip remains above the native page in both modes.
        private void BuildJustDropChrome()
        {
            Web.CarrierExtension = JustDropEscapeScript;
            var bar = new DockPanel();
            _justDropFullscreen = new Button { Name = "JustDropFullscreen", Margin = new Thickness(8, 5, 10, 5), MinWidth = 120 };
            if (this.TryFindResource("SmallPinkButton", out var theme) && theme is ControlTheme buttonTheme)
                _justDropFullscreen.Theme = buttonTheme;
            _justDropFullscreen.Click += (_, _) => SetJustDropFullscreen(WindowState != WindowState.FullScreen);
            DockPanel.SetDock(_justDropFullscreen, Dock.Right);
            bar.Children.Add(_justDropFullscreen);
            var pink = this.TryFindResource("PinkBrush", out var accent) ? accent as IBrush ?? Brushes.HotPink : Brushes.HotPink;
            var panel = this.TryFindResource("PanelBgBrush", out var background) ? background as IBrush ?? Brushes.Black : Brushes.Black;
            bar.Children.Add(new TextBlock { Text = Title, Margin = new Thickness(12, 0, 8, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeight.SemiBold,
                Foreground = pink, TextTrimming = TextTrimming.CharacterEllipsis });
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Background = Brushes.Black };
            grid.Children.Add(new Border { Background = panel, BorderBrush = pink,
                BorderThickness = new Thickness(0, 0, 0, 1), Child = bar });
            Grid.SetRow(Web, 1);
            grid.Children.Add(Web);
            Content = grid;
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) UpdateJustDropFullscreenButton(); };
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
                {
                    LeaveJustDropFullscreen();
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
            UpdateJustDropFullscreenButton();
        }

        private void UpdateJustDropFullscreenButton()
        {
            if (_justDropFullscreen == null) return;
            _justDropFullscreen.Content = Loc.Get(WindowState == WindowState.FullScreen ? "btn_exit_fullscreen" : "btn_fullscreen");
            ToolTip.SetTip(_justDropFullscreen, Loc.Get("tooltip_fill_the_screen_esc_brings_the_window_back"));
        }

        private void SetJustDropFullscreen(bool fullscreen)
        {
            if (fullscreen)
            {
                if (WindowState == WindowState.FullScreen) return;
                _justDropPreviousState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                WindowState = WindowState.FullScreen;
            }
            else LeaveJustDropFullscreen();
        }

        private void LeaveJustDropFullscreen()
        {
            if (WindowState == WindowState.FullScreen) WindowState = _justDropPreviousState;
        }

        private static bool IsJustDropEscape(string json)
        {
            try { return (string?)JObject.Parse(Unwrap(json))["type"] == "ccp-host-escape"; }
            catch { return false; }
        }

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
            Web.Navigate(PageUrl);
            Log.Information("[Game] justdrop: launched (shop)");   // never the url: it names the account
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
            window.SetJustDropFullscreen(true);
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
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { Log.Warning(ex, "[Game] justdrop: message handling failed"); }
        }
    }
}
