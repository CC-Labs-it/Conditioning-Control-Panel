using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Features;
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
    /// <para>Takeaway replay uses the bare player, fullscreen and panel duck/restore. Remaining:
    /// document-start hooks. The host-owned fullscreen strip is ported (#3340); the Chromium
    /// no-throttle flags reach WebView2 on Windows only (WebKitGTK has no equivalent).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string JustDropId = "justdrop";
        private WindowState _justDropWindowedState = WindowState.Normal;
        private PixelPoint _justDropPosition;
        private double _justDropWidth, _justDropHeight;
        private double _justDropWindowedWidth, _justDropWindowedHeight;
        private bool _justDropHasFrame, _justDropChangingFrame;

        private void CaptureJustDropFrame()
        {
            if (_justDropChangingFrame || !IsVisible || WindowState != WindowState.Normal || Width <= 0 || Height <= 0) return;
            _justDropPosition = Position;
            _justDropWidth = Width;
            _justDropHeight = Height;
            _justDropHasFrame = true;
        }

        private void CenterJustDrop(Visual source)
        {
            var screens = ScreenList.Enumerate(source);
            var window = source as Window ?? this;
            var position = window.Position;
            global::Avalonia.Platform.Screen? screen = null;
            // WPF TargetScreen :447-464 uses the monitor containing most of the HWND.
            // Use the native window query when available; injected screens use the same overlap rule.
            if (ScreenList.QueryOverride == null && window.IsVisible)
            {
                try { screen = window.Screens.ScreenFromWindow(window); }
                catch (Exception ex) { Log.Debug(ex, "[Game] justdrop: screen lookup failed"); }
            }
            if (screen == null || !screens.Contains(screen))
            {
                var size = window.FrameSize ?? window.ClientSize;
                var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1;
                double right = position.X + Math.Max(0, size.Width * scaling);
                double bottom = position.Y + Math.Max(0, size.Height * scaling);
                screen = screens.Select(s =>
                {
                    var b = s.Bounds;
                    double overlap = Math.Max(0, Math.Min(right, b.Right) - Math.Max(position.X, b.X))
                        * Math.Max(0, Math.Min(bottom, b.Bottom) - Math.Max(position.Y, b.Y));
                    double dx = Math.Max(0, Math.Max(b.X - right, position.X - b.Right));
                    double dy = Math.Max(0, Math.Max(b.Y - bottom, position.Y - b.Bottom));
                    return (Screen: s, Overlap: overlap, Distance: dx * dx + dy * dy);
                }).OrderByDescending(x => x.Overlap).ThenBy(x => x.Distance)
                  .ThenByDescending(x => x.Screen.IsPrimary).Select(x => x.Screen).FirstOrDefault();
            }
            if (screen == null) return;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            if (_justDropWindowedWidth <= 0 || _justDropWindowedHeight <= 0)
            {
                var primary = screens.FirstOrDefault(s => s.IsPrimary) ?? screen;
                var primaryScale = primary.Scaling > 0 ? primary.Scaling : 1;
                _justDropWindowedWidth = primary.Bounds.Width * .85 / primaryScale;
                _justDropWindowedHeight = primary.Bounds.Height * .85 / primaryScale;
            }
            Width = Math.Min(_justDropWindowedWidth, screen.Bounds.Width / scale);
            Height = Math.Min(_justDropWindowedHeight, screen.Bounds.Height / scale);
            Position = new PixelPoint(screen.Bounds.X + (int)((screen.Bounds.Width - Width * scale) / 2),
                screen.Bounds.Y + (int)((screen.Bounds.Height - Height * scale) / 2));
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        // WPF JustDropHostService.cs:132-135: audio starts and the live clock survives occlusion.
        internal const string JustDropBrowserArguments = "--autoplay-policy=no-user-gesture-required "
            + "--disable-background-timer-throttling --disable-backgrounding-occluded-windows";

        /// <summary>WPF ShowTab("justdrop"): a door the account cannot see is a no-op, never a redirect.</summary>
        private static bool JustDropDoorOpen()
        {
            if (JustDropService.DoorAvailable)
            {
                // WPF JustDropHostService.cs:72: count each shop open, including refocus, once.
                try { EmiState.NoteUsage(JustDropId); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] shop open not counted"); }
                return true;
            }
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

        private bool _handoffFellBack;

        /// <summary>WPF AttachAuthHeader's NavigationCompleted (JustDropHostService.cs:176-188, ccp-bugs #1499):
        /// a refused handoff navigates ONCE to the signed-out destination instead of showing the raw JSON.</summary>
        private void FallBackOnRefusedHandoff(Uri completed, string nextPath)
        {
            if (_handoffFellBack || !JustDropHandoff.IsRefusedHandoff(completed)) return;
            _handoffFellBack = true;
            Log.Warning("[Game] justdrop: sign-in handoff refused; opening signed-out");
            Web.Navigate(new Uri(JustDropHandoff.SiteOrigin + nextPath));
        }

        /// <summary>WPF Launch(ShopPath) (:75-158): windowed, the panel stays where it is (this is browsing).</summary>
        private void LoadJustDrop()
        {
            var nextPath = Spec.Query ?? JustDropHandoff.ShopPath;
            PageUrl = new Uri(JustDropHandoff.BuildStartUrl(nextPath, JustDropHandoff.UnifiedId(), JustDropHandoff.AuthToken()));
            Web.NavigationCompleted += url => FallBackOnRefusedHandoff(url, nextPath);
            Web.WindowsBrowserArguments = JustDropBrowserArguments;
            Web.WindowsUserDataFolder = System.IO.Path.Combine(CorePaths.UserData, "browser_data_avalonia_justdrop");
            Web.AllowNavigation = JustDropHandoff.IsSite;
            Web.RequestHeader = uri => JustDropHeaderFor(uri, JustDropHandoff.AuthToken());
            Web.InjectCarrier = true;   // the remote page speaks chrome.webview; WebKitGTK needs the shim
            // WPF JustDropHostService.cs:111,143,422: one asset map per window life, served on its core, cleared on close.
            var assets = JustDropAssets = new JustDropAssetMap();
            Web.ConfigureNative = handle => Platform.JustDropAssetWebView2.Attach(handle, assets);
            Closed += (_, _) => { JustDropAssets = null; try { assets.Clear(); } catch (Exception ex) { Log.Debug(ex, "[Game] justdrop: asset cleanup failed"); } };
            // WPF ChaosWebViewHost.cs:1227-1231,1562-1578: register before the first document.
            Web.WindowsDocumentStartScript = JustDropEscapeScript;
            // Keep the fallback for WebKit and for a failed WebView2 registration.
            Web.NavigationCompleted += uri =>
            {
                if (JustDropHandoff.IsSite(uri)) _ = Web.InvokeScriptAsync(JustDropEscapeScript);
            };
            BuildJustDropChrome();
            Web.Navigate(PageUrl);
            Log.Information("[Game] justdrop: launched (shop)");   // never the url: it names the account
        }

        internal const string JustDropEscapeScript =
            "(function(){if(window.__ccpHostEscape)return;window.__ccpHostEscape=true;" +
            "window.addEventListener('keydown',function(e){if(e.key!=='Escape'&&e.keyCode!==27)return;" +
            "try{window.chrome.webview.postMessage({type:'ccp-host-escape'});}catch(_){}},true);})();";

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
            toggle.Click += (_, _) => SetJustDropFullscreen(WindowState != WindowState.FullScreen);
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
                SetJustDropFullscreen(false);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        internal Action? JustDropActivatePanelForTests;

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
            window.CenterJustDrop(shell);
            window.SetJustDropFullscreen(true);
            window.Closed += (_, _) =>
            {
                if (duck && shell.IsVisible && shell.WindowState == global::Avalonia.Controls.WindowState.Minimized)
                {
                    shell.WindowState = state == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                    if (window.JustDropActivatePanelForTests is { } activate) activate();
                    else shell.Activate();
                }
            };
            window.Show();
            if (duck) { shell.WindowState = global::Avalonia.Controls.WindowState.Minimized; window.Activate(); }
            // WPF ChaosWebViewHost.cs:919-972: ownership attaches after the duck, so a minimized panel
            // leaves the replay unowned until it comes back (GameWindow.JustDropOwner.cs).
            window.AttachJustDropOwner(shell);
            return window;
        }

        // WPF ChaosWebViewHost.cs:713-785: preserve the last normal frame, including maximized state.
        private void SetJustDropFullscreen(bool fullscreen)
        {
            if (fullscreen)
            {
                if (WindowState == WindowState.FullScreen) return;
                _justDropWindowedState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                CaptureJustDropFrame();
                // WPF SetFullscreen :600-606 saves size only for a visible normal frame.
                if (IsVisible && WindowState == WindowState.Normal && _justDropHasFrame)
                {
                    _justDropWindowedWidth = _justDropWidth;
                    _justDropWindowedHeight = _justDropHeight;
                }
                WindowState = WindowState.FullScreen;
            }
            else if (WindowState == WindowState.FullScreen)
            {
                var screens = ScreenList.Enumerate(this);
                bool reachable = _justDropHasFrame && screens.Any(s =>
                    _justDropPosition.X < s.Bounds.Right && _justDropPosition.Y < s.Bounds.Bottom &&
                    _justDropPosition.X + _justDropWidth * s.Scaling > s.Bounds.X &&
                    _justDropPosition.Y + _justDropHeight * s.Scaling > s.Bounds.Y);
                _justDropChangingFrame = true;
                try
                {
                    WindowState = WindowState.Normal;
                    if (reachable)
                    {
                        Width = _justDropWidth; Height = _justDropHeight; Position = _justDropPosition;
                        if (_justDropWindowedState == WindowState.Maximized) WindowState = WindowState.Maximized;
                    }
                    else CenterJustDrop(this);
                }
                finally { _justDropChangingFrame = false; }
                CaptureJustDropFrame();
            }
        }

        internal JustDropAssetMap? JustDropAssets { get; private set; }

        /// <summary>WPF OnHostAsk assets-request (JustDropHostService.cs:309-321): exactly one 'assets' answer per ask,
        /// same kind, empty on a fault or where this engine cannot serve the urls (WebKitGTK).</summary>
        private void AnswerJustDropAssets(JObject o)
        {
            // The sender is the site: HandleMessage only runs for a JustDropHandoff.IsSite page (WPF IsSiteSource).
            if (!JustDropBridge.IsPageEnvelope(o) || JustDropBridge.ParseAssets(o["payload"] as JObject) is not { } ask) return;
            var assets = JustDropAssets;
            if (assets == null) return;
            _ = System.Threading.Tasks.Task.Run(() => Platform.JustDropAssetWebView2.Supported ? assets.List(ask.Video, ask.Max) : new())
                .ContinueWith(t =>
                {
                    if (JustDropAssets != assets) return;
                    var kind = ask.Video ? "video" : "image";
                    var urls = t.IsFaulted ? new System.Collections.Generic.List<string>() : t.Result;
                    Post(JustDropBridge.Envelope("assets", new { kind, items = urls.Select(url => new { url, kind }).ToArray() }));
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>WPF OnPageMessage (:227): the raw envelope to Core, so the source/version checks stay in one place.</summary>
        private void HandleJustDrop(JObject o)
        {
            if ((string?)o["type"] == "ccp-host-escape")
            {
                SetJustDropFullscreen(false);
                return;
            }
            if ((string?)o["type"] == "assets-request") { AnswerJustDropAssets(o); return; }
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { Log.Warning(ex, "[Game] justdrop: message handling failed"); }
        }
    }
}
