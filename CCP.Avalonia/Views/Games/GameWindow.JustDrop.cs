using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
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
    /// <para>Takeaway replay uses the bare player (<see cref="Game.Query"/>), fullscreen, the host strip and
    /// panel duck/restore. ponytail: native document-start/fullscreen hooks still need a WebHost engine seam;
    /// Chromium no-throttle flags apply on WebView2 only (WebKitGTK has no equivalent).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string JustDropId = "justdrop";

        internal const string JustDropBrowserArguments = "--autoplay-policy=no-user-gesture-required "
            + "--disable-background-timer-throttling --disable-backgrounding-occluded-windows";

        private Button? _justDropFullscreen;
        private WindowState _justDropWindowedState;
        private Window? _justDropDuckedOwner;
        private WindowState _justDropOwnerState;

        /// <summary>WPF LaunchReplay: a kept receipt opens the bare player, even with the shop withheld.</summary>
        internal static GameWindow? LaunchJustDropReplay(string orderCode, Window owner)
        {
            if (string.IsNullOrWhiteSpace(orderCode)) return null;
            GameWindow? live;
            lock (Open) live = Open.FirstOrDefault(w => w.Spec.Id == JustDropId);
            if (live != null) { live.Activate(); return live; }
            var window = new GameWindow(Games[JustDropId] with { Query = JustDropHandoff.ReplayPath(orderCode) });
            window.Load(Server());
            window.Show(owner);
            window.SetJustDropFullscreen(true);
            if (owner.IsVisible && owner.WindowState != WindowState.Minimized)
            {
                window._justDropDuckedOwner = owner;
                window._justDropOwnerState = owner.WindowState;
                owner.WindowState = WindowState.Minimized;
                window.Activate();
            }
            return window;
        }

        /// <summary>WPF BuildHostChrome: native chrome remains above the page in both window modes.</summary>
        private void InitializeJustDropChrome()
        {
            _justDropFullscreen = new Button
            {
                Name = "JustDropFullscreen", MinWidth = 120, Margin = new Thickness(8, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (Application.Current?.TryFindResource("SmallPinkButton", out var theme) == true
                && theme is global::Avalonia.Styling.ControlTheme buttonTheme) _justDropFullscreen.Theme = buttonTheme;
            ToolTip.SetTip(_justDropFullscreen, Loc.Get("tooltip_fill_the_screen_esc_brings_the_window_back"));
            _justDropFullscreen.Click += (_, _) => SetJustDropFullscreen(WindowState != WindowState.FullScreen);
            var bar = new DockPanel();
            DockPanel.SetDock(_justDropFullscreen, Dock.Right);
            bar.Children.Add(_justDropFullscreen);
            bar.Children.Add(new TextBlock
            {
                Text = "Just Drop", Margin = new Thickness(12, 0, 8, 0), FontSize = 12,
                FontWeight = FontWeight.SemiBold, Foreground = this.TryFindResource("PinkBrush", out var pink) && pink is IBrush brush ? brush : Brushes.HotPink,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var strip = new Border
            {
                Background = this.TryFindResource("PanelBgBrush", out var bg) && bg is IBrush panel ? panel : Brushes.Black,
                BorderBrush = this.TryFindResource("TransparentPink40Brush", out var border) && border is IBrush accent ? accent : Brushes.HotPink,
                BorderThickness = new Thickness(0, 0, 0, 1), Child = bar,
            };
            var frame = new DockPanel();
            DockPanel.SetDock(strip, Dock.Top);
            frame.Children.Add(strip);
            frame.Children.Add(Web);
            Content = frame;
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape || WindowState != WindowState.FullScreen) return;
                SetJustDropFullscreen(false);
                e.Handled = true;
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Activated += (_, _) => { if (WindowState == WindowState.FullScreen) Topmost = true; };
            Deactivated += (_, _) => Topmost = false;
            PropertyChanged += (_, e) =>
            {
                if (e.Property == WindowStateProperty) UpdateJustDropToggle();
            };
            Closed += (_, _) =>
            {
                var owner = _justDropDuckedOwner;
                _justDropDuckedOwner = null;
                if (owner is { IsVisible: true, WindowState: WindowState.Minimized })
                {
                    owner.WindowState = _justDropOwnerState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                    owner.Activate();
                }
                // the drawer invalidate + shelf refresh live in GameWindow's ctor (one Closed for both doors)
            };
            UpdateJustDropToggle();
        }

        internal void SetJustDropFullscreen(bool fullscreen)
        {
            if (fullscreen == (WindowState == WindowState.FullScreen)) return;
            if (fullscreen)
            {
                _justDropWindowedState = WindowState;
                WindowState = WindowState.FullScreen;
                Topmost = true;
            }
            else
            {
                Topmost = false;
                WindowState = _justDropWindowedState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            }
            UpdateJustDropToggle();
        }

        private void UpdateJustDropToggle()
        {
            if (_justDropFullscreen != null)
                _justDropFullscreen.Content = Loc.Get(WindowState == WindowState.FullScreen ? "btn_exit_fullscreen" : "btn_fullscreen");
        }

        /// <summary>WPF ShowTab("justdrop"): a door the account cannot see is a no-op, never a redirect.</summary>
        private static bool JustDropDoorOpen()
        {
            if (JustDropService.DoorAvailable) { EmiState.NoteUsage(JustDropId); return true; }
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
            Web.WindowsBrowserArguments = JustDropBrowserArguments;
            Web.WindowsUserDataFolder = System.IO.Path.Combine(CorePaths.UserData, "browser_data_justdrop");
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
