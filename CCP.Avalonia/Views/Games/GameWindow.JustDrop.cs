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
    /// <para>Takeaway replay uses the bare player, fullscreen and panel duck/restore. The window's
    /// fullscreen is the host's (WPF HostOwnedFullscreen): a strip with the toggle in both modes, Esc from
    /// either focus state, Topmost rented while active. Remaining: native ownership by the panel and
    /// Chromium's autoplay/no-throttle flags (WebKitGTK has no equivalent).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string JustDropId = "justdrop";

        /// <summary>WPF ChaosWebViewHost HostEscapeMessageType (:1231): consumed by the host, never the page's.</summary>
        internal const string HostEscapeMessageType = "ccp-host-escape";

        /// <summary>WPF EscapeBridgeScript (:1236), Esc route 2: a focused page owns the keyboard, so it
        /// reports Esc (capture phase, no preventDefault). Injected after each load (no document-start seam).</summary>
        internal const string EscapeBridgeScript =
            "(function(){try{if(window.__ccpHostEsc)return;window.__ccpHostEsc=1;window.addEventListener('keydown',function(e){" +
            "if(e.key!=='Escape'&&e.keyCode!==27)return;" +
            "try{window.chrome.webview.postMessage({type:'" + HostEscapeMessageType + "'});}catch(_){}" +
            "},true);}catch(_){}})();";

        /// <summary>The strip's toggle (WPF _fsToggle); null on every other game.</summary>
        internal Button? FullscreenToggle { get; private set; }

        /// <summary>WPF BuildHostChrome (:627-662): title left, toggle right, above the page in BOTH modes,
        /// because in fullscreen it is the only exit there is.</summary>
        private Control BuildJustDropChrome()
        {
            var toggle = FullscreenToggle = new Button
            {
                Margin = new Thickness(8, 5, 10, 5),
                MinWidth = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock(),   // a TextBlock, so Avalonia never reads '_' as an access key
            };
            if (Res("SmallPinkButton") is ControlTheme theme) toggle.Theme = theme;
            ToolTip.SetTip(toggle, Loc.Get("tooltip_fill_the_screen_esc_brings_the_window_back"));
            toggle.Click += (_, _) => SetJustDropFullscreen(WindowState != WindowState.FullScreen);
            DockPanel.SetDock(toggle, Dock.Right);
            var bar = new DockPanel { LastChildFill = true };
            bar.Children.Add(toggle);
            bar.Children.Add(new TextBlock
            {
                Text = Title,
                Margin = new Thickness(12, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Res("PinkBrush") as IBrush ?? Brushes.HotPink,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var strip = new Border
            {
                Background = Res("PanelBgBrush") as IBrush ?? Brushes.Black,
                BorderBrush = Res("TransparentPink40Brush") as IBrush ?? Brushes.HotPink,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = bar,
            };
            DockPanel.SetDock(strip, Dock.Top);
            UpdateJustDropToggle();
            PropertyChanged += (_, e) =>
            {
                if (e.Property != WindowStateProperty) return;
                UpdateJustDropToggle();
                // The window manager can leave fullscreen on its own: the rental ends with it.
                Topmost = WindowState == WindowState.FullScreen && IsActive;
            };
            // Esc route 1 (WPF HookHostFullscreenInput :694-704): while the host holds the keyboard.
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape || WindowState != WindowState.FullScreen) return;
                SetJustDropFullscreen(false);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            // Topmost is rented, not owned (#905): a modal must never end up alive behind the page.
            Activated += (_, _) => { if (WindowState == WindowState.FullScreen) Topmost = true; };
            Deactivated += (_, _) => Topmost = false;
            Web.NavigationCompleted += url => _ = Web.InvokeScriptAsync(EscapeBridgeScript);
            var root = new DockPanel { Background = Brushes.Black };
            root.Children.Add(strip);
            root.Children.Add(Web);
            return root;
        }

        private static object? Res(string key) =>
            global::Avalonia.Application.Current?.TryFindResource(key, out var v) == true ? v : null;

        /// <summary>WPF SetFullscreen under HostOwnedFullscreen: only the toggle and Esc call it, never the page.</summary>
        internal void SetJustDropFullscreen(bool on)
        {
            WindowState = on ? WindowState.FullScreen : WindowState.Normal;
            Topmost = on;
            if (on) Activate();
        }

        /// <summary>WPF UpdateChromeToggle: the label can never disagree with the window.</summary>
        private void UpdateJustDropToggle()
        {
            if (FullscreenToggle?.Content is TextBlock label)
                label.Text = Loc.Get(WindowState == WindowState.FullScreen ? "btn_exit_fullscreen" : "btn_fullscreen");
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
            window.WindowState = WindowState.FullScreen;
            // WPF RestoreMainWindow (:262-280): Maximized comes back Maximized, anything else Normal, then activated.
            window.Closed += (_, _) =>
            {
                if (!duck || !shell.IsVisible || shell.WindowState != WindowState.Minimized) return;
                shell.WindowState = state == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                try { shell.Activate(); } catch (Exception ex) { Log.Debug("[Game] justdrop: restore activate: {E}", ex.Message); }
            };
            window.Show();
            if (duck) { shell.WindowState = WindowState.Minimized; window.Activate(); }
            return window;
        }

        /// <summary>WPF OnPageMessage (:227): the raw envelope to Core, so the source/version checks stay in one place.</summary>
        private void HandleJustDrop(JObject o)
        {
            if ((string?)o["type"] == HostEscapeMessageType)   // host chrome, not page business (WPF :1724-1729)
            {
                if (WindowState == WindowState.FullScreen) SetJustDropFullscreen(false);
                return;
            }
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { Log.Warning(ex, "[Game] justdrop: message handling failed"); }
        }
    }
}
