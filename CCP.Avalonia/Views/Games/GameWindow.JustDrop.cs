using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
    /// Document-start Escape injection and Chromium no-throttle flags (WebKitGTK has no equivalent).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string JustDropId = "justdrop";

        private Button? _justDropFullscreenToggle;
        private WindowState _justDropWindowedState = WindowState.Normal;

        // WPF ChaosWebViewHost.cs:1227-1231, 1571: do not swallow the page's own Escape.
        internal const string JustDropEscapeScript =
            "(function(){if(window.__ccpHostEscape)return;window.__ccpHostEscape=true;" +
            "window.addEventListener('keydown',function(e){if(e.key!=='Escape'&&e.keyCode!==27)return;" +
            "try{window.chrome.webview.postMessage({type:'ccp-host-escape'});}catch(_){}},true);})();";

        // WPF ChaosWebViewHost.cs:257-269, 621-693: the strip stays above the native page in both modes.
        private void BuildJustDropChrome()
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            var bar = new DockPanel { LastChildFill = true };
            _justDropFullscreenToggle = new Button
            {
                Name = "JustDropFullscreen", MinWidth = 120, Margin = new global::Avalonia.Thickness(8, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _justDropFullscreenToggle.Click += (_, _) => SetJustDropFullscreen(WindowState != WindowState.FullScreen);
            DockPanel.SetDock(_justDropFullscreenToggle, Dock.Right);
            bar.Children.Add(_justDropFullscreenToggle);
            bar.Children.Add(new TextBlock
            {
                Text = "Just Drop", Margin = new global::Avalonia.Thickness(12, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.HotPink, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            grid.Children.Add(new Border
            {
                Background = Brushes.Black, BorderBrush = Brushes.HotPink,
                BorderThickness = new global::Avalonia.Thickness(0, 0, 0, 1), Child = bar,
            });
            Content = null;
            Grid.SetRow(Web, 1);
            grid.Children.Add(Web);
            Content = grid;
            void Update()
            {
                _justDropFullscreenToggle.Content = Loc.Get(WindowState == WindowState.FullScreen ? "btn_exit_fullscreen" : "btn_fullscreen");
                ToolTip.SetTip(_justDropFullscreenToggle, Loc.Get("tooltip_fill_the_screen_esc_brings_the_window_back"));
            }
            PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) Update(); };
            EventHandler language = (_, _) => Update();
            LocalizationManager.Instance.LanguageChanged += language;
            Closed += (_, _) => LocalizationManager.Instance.LanguageChanged -= language;
            Update();
            // WPF :698-715: both host focus and web focus can leave fullscreen; rent Topmost only while active.
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape || WindowState != WindowState.FullScreen) return;
                SetJustDropFullscreen(false); e.Handled = true;
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Activated += (_, _) => Topmost = WindowState == WindowState.FullScreen;
            Deactivated += (_, _) => Topmost = false;
            Web.NavigationCompleted += url =>
            {
                if (JustDropHandoff.IsSite(url)) _ = Web.InvokeScriptAsync(JustDropEscapeScript);
            };
        }

        // WPF :722-773: Avalonia owns native fullscreen and restores the previous windowed state.
        private void SetJustDropFullscreen(bool fullscreen)
        {
            if (fullscreen == (WindowState == WindowState.FullScreen)) return;
            if (fullscreen) _justDropWindowedState = WindowState;
            WindowState = fullscreen ? WindowState.FullScreen : _justDropWindowedState;
            Topmost = fullscreen && IsActive;
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
            BuildJustDropChrome();
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
            if ((string?)o["type"] == "ccp-host-escape") { SetJustDropFullscreen(false); return; }
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { Log.Warning(ex, "[Game] justdrop: message handling failed"); }
        }
    }
}
