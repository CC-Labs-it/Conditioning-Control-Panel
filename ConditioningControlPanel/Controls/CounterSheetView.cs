using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services.PrizeCounter;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// THE COUNTER SHEET inside a door window (owner 2026-11-17: "a Tab inside the window that smoothly
/// comes down", then "a bit smaller ... a clear border"). One per window with a Sparkles wallet bar (the
/// launcher, the panel), laid over that window's root grid: a scrim over everything under the bar, and
/// the sheet itself, inset well inside the window (<see cref="CounterSheetRules.Frame"/>) as one object:
/// night ground, a gold edge, rounded corners.
///
/// <para>AIRSPACE. The page is a WebView2, a native child HWND: WPF transforms, opacity and clips never
/// reach it, and the referenced package (1.0.2535.41) has no composition control. So the frame and the
/// page slide together by layout margin (a move, never a resize: the page never reflows mid-slide), the
/// HWND sits inside the frame's padding, and a WINDOW REGION rounds its corners to the frame's inside and
/// cuts whatever of it is still above the bar line. Motion Off cuts, Reduced halves, Gentle never slides
/// and only fades the scrim.</para>
///
/// <para>The WebView2 is created once, shortly after the window shows (<see cref="CounterSheetRules.WarmDelayMs"/>), and
/// kept hidden with the page loaded and waiting for its <c>init</c>, so a first open is instant. After a close the SAME
/// page is reset (CONTRACT.md "Reuse") and waits, quiet, for the next init: no load between opens. A press that beats
/// the page slides on a waiting card in the page's ground until it has painted. Both doors share one browser profile (<see cref="UserDataFolderName"/>) and
/// one environment, with one constant argument string. The page bridge mirrors ChaosWebViewHost's:
/// navigation locked to ccp.game, <c>ready</c> then queued frames, <c>log</c> to the app log, every
/// other frame to the open counter only (<see cref="PrizeCounterHostService"/>, via
/// <see cref="SheetCounterSurface"/>).</para>
/// </summary>
internal sealed class CounterSheetView : Grid
{
    public const string UserDataFolderName = "browser_data_prizesheet";

    /// <summary>Constant on purpose: one argument string per user-data folder.</summary>
    public const string BrowserArguments =
        "--disable-direct-composition-video-overlays --disable-features=CalculateNativeWinOcclusion --autoplay-policy=no-user-gesture-required";

    /// <summary>The reason a sheet gives when its page died (the counter closes as failed).</summary>
    public const string WhyFailed = "failed";

    private static readonly TimeSpan WarmDelay = TimeSpan.FromMilliseconds(CounterSheetRules.WarmDelayMs);

    /// <summary>sheet.css <c>--night</c> (the cartoon page's ground): the browser's default background and the
    /// frame's, so a frame before the page's first paint is its own ground, never black or white.</summary>
    private static readonly Color Night = Color.FromRgb(0x2A, 0x17, 0x40);

    /// <summary>sheet.css <c>--gold</c>, the wallet's gold: the sheet's edge.</summary>
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xD2, 0x4A);

    /// <summary>The cartoon page's ink (near-black plum): the outline.</summary>
    private static readonly Color Ink = Color.FromRgb(0x1C, 0x0A, 0x22);

    /// <summary>sheet.css <c>--night-3</c>: the raised slab's side, down-right of the frame. An ink shadow vanishes
    /// on the dark window; a lighter plum edge with its own ink line reads as the panel standing off it.</summary>
    private static readonly Color Slab = Color.FromRgb(0x45, 0x28, 0x6A);

    /// <summary>The longest a warm page's reveal waits for its first shop answer to paint (then it slides anyway).</summary>
    private static readonly TimeSpan RevealCap = TimeSpan.FromMilliseconds(150);

    /// <summary>After the page's first answer is posted: two frames for it to paint before the slide starts.</summary>
    private static readonly TimeSpan RevealAfterReply = TimeSpan.FromMilliseconds(40);

    private static Task<CoreWebView2Environment>? _env;

    private readonly FrameworkElement _bar;
    private readonly string _from;
    private readonly Border _scrim;
    private readonly Grid _slot;
    private readonly Border _frame, _shadow;
    private readonly FrameClock _clock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _watch = new();
    private readonly DispatcherTimer _reveal = new(DispatcherPriority.Normal);
    /// <summary>While the sheet is down a card can still arrive (a server announcement does not wait for the
    /// ladder; seen on screen over the sheet): a cheap look at the door's owned windows twice a second.</summary>
    private readonly DispatcherTimer _cardWatch = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _revealHeld, _down;
    private Action<bool>? _pressChip;
    /// <summary>The door's own floating cards hidden while the sheet is down (restored when it is up again).</summary>
    private readonly List<Window> _hiddenCards = new();
    private WebView2? _web;
    private CoreWebView2? _core;
    private bool _disposed, _noRuntime;
    private Func<FrameworkElement?>? _walletChip;
    private FrameworkElement? WalletStarNow => _walletChip?.Invoke() is { } chip ? (FrameworkElement?)SparkleWallet.FindStar(chip) ?? chip : null;
    private (int X, int Y)? _walletSent;

    // The page: which document, whether it said ready, whether a counter owns it.
    private string? _pageUrl;
    private bool _pageReady, _fresh, _live;
    /// <summary>The kept page served an open since its last load or reset; a reset was sent and its ready is awaited.</summary>
    private bool _used, _resetting;
    /// <summary>Page loads so far, opens served by the current load, and the open that last called Load.</summary>
    private int _loads, _served, _loadedFor;
    private readonly DispatcherTimer _resetWatch = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(CounterSheetRules.ResetTimeoutMs) };
    private bool _warms, _warmPending;
    // This open: the page has not painted yet (the HWND stays cut, the waiting card shows), and its timings.
    private bool _waiting;
    private readonly Stopwatch _openWatch = new();
    private long _slideAtMs = -1;
    private string _how = "cold";
    private readonly Border _placeholder;
    private readonly ScaleTransform _starPulse = new(1, 1);
    private int _session, _sessions;
    private Action? _onReady;
    private Action<JObject>? _onMessage;
    private Action<string>? _wantsClose;
    private readonly List<string> _pending = new();

    // The slide.
    private double _p, _from0, _to, _scrim0, _scrimP, _settleOff = double.NaN;
    private bool _openOnHandOver;
    private int _slideMs, _scrimMs, _leadMs;
    private (int, int, int, int) _region = (-1, -1, -1, -1);

    public Window DoorWindow { get; }

    /// <summary>The dim layer under the sheet (a game window's top strip copies its brush and opacity).</summary>
    internal Border ScrimLayer => _scrim;

    /// <summary>False in a game window: the game's own close hook hands the keyboard back, the sheet must not take it.</summary>
    internal bool FocusDoorOnClose { get; set; } = true;

    /// <summary>The sheet is fully up again (slid away or cut): a game window shows its page again.</summary>
    internal event Action? Retracted;

    private CounterSheetView(Window door, FrameworkElement bar, string from)
    {
        DoorWindow = door;
        _bar = bar;
        _from = from;
        Grid.SetRowSpan(this, 99);
        Grid.SetColumnSpan(this, 99);
        _scrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x07, 0x04, 0x0C)),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
        };
        _scrim.MouseLeftButtonDown += (_, e) => { e.Handled = true; _wantsClose?.Invoke("scrim"); };
        // The sheet's edge (owner 2026-11-17: smooth edges, a clear border): night ground, gold line,
        // rounded corners, round the page's HWND (whose own region is rounded to match). It lights up
        // once the page has landed and goes out as the close starts: WPF composes behind a moving HWND
        // (measured about 40 DIP behind mid-slide in the launcher), so an edge that moved with the page
        // would trail it. The page itself, rounded and on the same night ground, does the slide.
        // Cartoon frame (shop frame lane): ink outline, gold band, night ground; a raised plum slab down-right
        // (polish lane: the ink shadow was invisible on the dark window, the gold tongue read as a stray block
        // and is gone; the pressed-in wallet chip is the door now), IN with the edge and OUT at once with it.
        var ink = new SolidColorBrush(Ink);
        _frame = new Border
        {
            Background = new SolidColorBrush(Night),
            BorderBrush = ink,
            BorderThickness = new Thickness(CounterSheetRules.InkDip),
            CornerRadius = new CornerRadius(CounterSheetRules.CornerRadiusDip),
            Margin = new Thickness(0, CounterSheetRules.GapUnderBarDip, 0, 0),
            Opacity = 0,
            IsHitTestVisible = false,
            Child = new Border
            {
                BorderBrush = new SolidColorBrush(Gold),
                BorderThickness = new Thickness(CounterSheetRules.BorderDip - CounterSheetRules.InkDip),
                CornerRadius = new CornerRadius(CounterSheetRules.CornerRadiusDip - CounterSheetRules.InkDip),
            },
        };
        _shadow = new Border
        {
            Background = new SolidColorBrush(Slab), BorderBrush = ink, BorderThickness = new Thickness(CounterSheetRules.InkDip),
            CornerRadius = new CornerRadius(CounterSheetRules.CornerRadiusDip), Opacity = 0, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        _slot = new Grid { ClipToBounds = true, Visibility = Visibility.Collapsed };
        _slot.Children.Add(_frame);
        _placeholder = MakePlaceholder();
        _slot.Children.Add(_placeholder);
        _resetWatch.Tick += (_, _) => OnResetTimeout();
        Children.Add(_scrim);
        Children.Add(_shadow);
        Children.Add(_slot);
        SizeChanged += (_, _) => OnResized();
        _clock.Tick += (_, _) => Step();
        _reveal.Tick += (_, _) => Reveal();
        _cardWatch.Tick += (_, _) => { if (_down) HideCards(); else _cardWatch.Stop(); };
        door.IsVisibleChanged += OnDoorVisibleChanged;
        door.Closed += OnDoorClosed;
    }

    /// <summary>Lay a sheet over <paramref name="host"/> (the window's root grid) under <paramref name="bar"/>,
    /// and warm its page a few seconds after the window is up. <paramref name="insertAt"/> keeps the
    /// window's own top layers (veil, edge) above the scrim; -1 adds it on top with <paramref name="zIndex"/>.</summary>
    public static CounterSheetView Mount(Window door, Panel host, FrameworkElement bar, string from, int insertAt = -1, int zIndex = 0,
        Func<FrameworkElement?>? walletChip = null, bool warm = true)
    {
        var view = new CounterSheetView(door, bar, from) { _walletChip = walletChip, _warms = warm };
        if (walletChip != null) view._pressChip = on => PressChip(walletChip(), on);
        if (zIndex != 0) Panel.SetZIndex(view, zIndex);
        if (insertAt >= 0 && insertAt <= host.Children.Count) host.Children.Insert(insertAt, view);
        else host.Children.Add(view);
        view.ScheduleWarm();
        return view;
    }

    // ============================== warm ==============================

    /// <summary>Make and load the page <see cref="WarmDelay"/> after the door window shows (again when a hidden door
    /// shows later), so the first press finds it ready. A game sheet never warms (it opens cold).</summary>
    private void ScheduleWarm()
    {
        if (!_warms || _warmPending || _web != null || _disposed) return;
        _warmPending = true;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = WarmDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _warmPending = false;
            if (_disposed || _web != null || !DoorWindow.IsVisible) return;   // a hidden door warms when it shows
            var sw = Stopwatch.StartNew();
            EnsureWeb();
            App.Logger?.Information("PrizeSheet: warming the {From} page ({Ms} ms on the UI thread)", _from, sw.ElapsedMilliseconds);
#if DEBUG
            DemoLater();
#endif
        };
        timer.Start();
    }

    private void EnsureWeb()
    {
        if (_web != null || _disposed || _noRuntime) return;
        try
        {
            string? version = null;
            try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch (Exception ex) { Diag.Swallowed(ex); }
            if (string.IsNullOrEmpty(version)) { _noRuntime = true; App.Logger?.Information("PrizeSheet: no WebView2 runtime, the counter window opens instead"); return; }
            // Parked above the window until the first layout cuts it to an empty region (ApplyProgress at 0).
            var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(Night.R, Night.G, Night.B), Margin = new Thickness(0, -20000, 0, 20000) };
            web.SizeChanged += (_, _) => ApplyProgress();
            _web = web;
            _slot.Children.Add(web);
            // The slot stays VISIBLE while the sheet is up, the page's HWND cut to an empty region: a collapsed
            // WebView2 is hidden to the browser, which drops its frames, and the next open showed flat black
            // (#121212) for a few hundred ms before the first paint (polish lane, seen on screen).
            _slot.Visibility = Visibility.Visible;
            _ = InitCoreAsync(web);
        }
        catch (Exception ex) { App.Logger?.Warning("PrizeSheet: WebView2 create failed: {E}", ex.Message); DisposeWeb(); }
    }

    private static Task<CoreWebView2Environment> SharedEnvironment()
    {
        if (_env is { IsFaulted: false, IsCanceled: false } env) return env;
        var folder = Path.Combine(App.UserDataPath, UserDataFolderName);
        Directory.CreateDirectory(folder);
        return _env = CoreWebView2Environment.CreateAsync(null, folder,
            new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = BrowserArguments });
    }

    private async Task InitCoreAsync(WebView2 web)
    {
        try
        {
            var env = await SharedEnvironment().ConfigureAwait(true);
            await web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            if (_disposed || !ReferenceEquals(web, _web) || web.CoreWebView2 == null) return;
            var core = web.CoreWebView2;
            global::ConditioningControlPanel.Services.Fyp.Online.ScrolllerWebGate.Attach(core);
            var s = core.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsBuiltInErrorPageEnabled = false;
            s.IsWebMessageEnabled = true;
            // The page is our own files on ccp.game (navigation locked there): a SmartScreen reputation check of that
            // made-up host held every load of it for about 2 s before the document committed (open speed lane).
            try { s.IsReputationCheckingRequired = false; } catch (Exception ex) { Diag.Swallowed(ex); }
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            foreach (var (host, folder, access) in new[] { ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny), ChaosWebViewHost.ContentMapping() })
                if (Directory.Exists(folder)) core.SetVirtualHostNameToFolderMapping(host, folder, access);
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.WebMessageReceived += OnWebMessageReceived;
            core.ProcessFailed += OnProcessFailed;
            _core = core;
            Navigate(_pageUrl ?? CounterSheetRules.SheetUrl);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("PrizeSheet: WebView2 init failed: {E}", ex.Message);
            if (ReferenceEquals(web, _web)) { DisposeWeb(); _wantsClose?.Invoke(WhyFailed); }
        }
    }

    // ============================== the page ==============================

    private void Navigate(string url)
    {
        _pageUrl = url;
        _pageReady = false;
        _live = false;
        _used = false;
        _resetting = false;
        _resetWatch.Stop();
        _loads++;
        _served = 0;
        _fresh = _session == 0;   // nobody owns it yet: the next open takes it as it stands
        try { _core?.Navigate(url); }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet: navigate failed: {E}", ex.Message); }
    }

    /// <summary>Hand the page to this open: a warm page that has not met a counter yet is taken as it
    /// stands (its <c>ready</c> fires now); anything else loads <paramref name="url"/> again.</summary>
    public void Load(int session, string url, Action ready, Action<JObject> message)
    {
        if (session == 0 || session != _session || _disposed) return;
        _onReady = ready;
        _onMessage = message;
        _pending.Clear();
        _live = false;
        bool first = _loadedFor != session;   // a second Load in one open (a new tab, a sign-in) always loads again
        _loadedFor = session;
        if (_core == null) { _pageUrl = url; return; }   // InitCoreAsync navigates it
        if (first && _pageUrl != null && ChaosWebViewHost.SameDocument(_pageUrl, url))
        {
            if (_fresh)
            {
                _fresh = false;
                if (_pageReady) Dispatcher.BeginInvoke(new Action(() => HandOver(session)));
                return;
            }
            // Opened again while the last open was still sliding up: that page starts over now, no load.
            if (CounterSheetRules.NeedsReset(_pageReady, _used)) { SendReset(); return; }
            if (_resetting) return;   // its ready hands it over
        }
        Navigate(url);
    }

    private void HandOver(int session)
    {
        if (session == 0 || session != _session || !_pageReady || _live || _core == null) return;
        _live = true;
        if (!_used) { _used = true; _served++; }
        _walletSent = WalletAt();   // init carried it
        foreach (var json in _pending)
            try { _core.PostWebMessageAsJson(json); } catch (Exception ex) { Diag.Swallowed(ex); }
        _pending.Clear();
        try { _onReady?.Invoke(); } catch (Exception ex) { Diag.Swallowed(ex); }
        // A warm page paints its shelves once its first shop answer lands: the reveal waits for that answer
        // plus two frames, never longer than RevealCap (then it slides anyway), so the sheet never slides
        // down as bare ground. A sheet that already slid on the waiting card hands over to the page the same way.
        if (_openOnHandOver || _waiting) HoldReveal(RevealCap);
    }

    private void HoldReveal(TimeSpan wait)
    {
        _revealHeld = true;
        _reveal.Stop();
        _reveal.Interval = wait;
        _reveal.Start();
    }

    private void Reveal()
    {
        _reveal.Stop();
        if (!_revealHeld) return;
        _revealHeld = false;
        if (_session == 0 || _disposed) return;
        bool held = _openOnHandOver;
        _openOnHandOver = false;
        ShowPage();
        if (held) AnimateTo(1);
        // One line per open (tester logs): press to slide start, press to the page's first content paint.
        if (_openWatch.IsRunning)
        {
            App.Logger?.Information("PrizeSheet: open from {From} ({How}, load {Load} open {Open}): slide +{Slide} ms, content +{Paint} ms",
                _from, _how, _loads, _served, _slideAtMs, _openWatch.ElapsedMilliseconds);
            _openWatch.Reset();
        }
    }

    /// <summary>The page has painted this open's content: its HWND shows again over the waiting card, which goes a
    /// frame later (the page's ground is the card's ground, so the hand-over never flashes).</summary>
    private void ShowPage()
    {
        if (!_waiting) return;
        _waiting = false;
        _region = (-1, -1, -1, -1);
        ApplyProgress();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_waiting) HidePlaceholder(); }));
    }

    private void HidePlaceholder()
    {
        _starPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _starPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _placeholder.Visibility = Visibility.Collapsed;
    }

    /// <summary>The waiting card: the page's night ground, its title band and a gold star with a slow pulse (Off and
    /// Gentle hold it still). No words: it is there for a moment.</summary>
    private Border MakePlaceholder()
    {
        var ink = new SolidColorBrush(Ink);
        var star = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 12,0 L 15.4,7.6 23.6,8.4 17.4,13.9 19.2,22 12,17.8 4.8,22 6.6,13.9 0.4,8.4 8.6,7.6 Z"),
            Fill = new SolidColorBrush(Gold), Stroke = ink, StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round,
            Width = 46, Height = 46, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _starPulse,
        };
        double r = CounterSheetRules.InnerRadiusDip;
        var band = new Border
        {
            Height = 64, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Slab),
            BorderBrush = ink, BorderThickness = new Thickness(0, 0, 0, CounterSheetRules.InkDip), CornerRadius = new CornerRadius(r, r, 0, 0),
        };
        var g = new Grid();
        g.Children.Add(band);
        g.Children.Add(star);
        return new Border
        {
            Background = new SolidColorBrush(Night), CornerRadius = new CornerRadius(r), Child = g,
            Visibility = Visibility.Collapsed, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    private void ShowPlaceholder()
    {
        _placeholder.Visibility = Visibility.Visible;
        var (amp, ms) = CounterSheetRules.StarPulse(Services.MotionFx.Level, Services.Safety.GentleVisuals.On);
        if (amp <= 0) return;
        var pulse = new System.Windows.Media.Animation.DoubleAnimation(1 - amp, 1 + amp, TimeSpan.FromMilliseconds(ms))
        {
            AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut },
        };
        _starPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        _starPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    // ============================== reuse (one page, many opens) ==============================

    /// <summary>After a close: a page that served the open is told to start over (CONTRACT "Reuse": it drops that
    /// open, goes quiet and says ready again); a page still loading, or ready and never handed over, is kept as is.
    /// Either way the next open takes it as it stands.</summary>
    private void KeepPage()
    {
        if (_core == null || _session != 0) return;
        _fresh = true;
        if (CounterSheetRules.NeedsReset(_pageReady, _used)) SendReset();
    }

    private void SendReset()
    {
        if (_core == null) return;
        _pageReady = false;
        _used = false;
        _resetting = true;
        try { _core.PostWebMessageAsJson("{\"type\":\"reset\"}"); }
        catch (Exception ex) { Diag.Swallowed(ex); OnResetTimeout(); return; }
        _resetWatch.Stop();
        _resetWatch.Start();
    }

    /// <summary>The page never answered its reset: load it again (an open waiting on it gets it when that load is ready).</summary>
    private void OnResetTimeout()
    {
        _resetWatch.Stop();
        if (!_resetting || _core == null) return;
        App.Logger?.Information("PrizeSheet: the {From} page did not answer its reset, loading it again", _from);
        if (_openOnHandOver) { _openOnHandOver = false; _waiting = true; ShowPlaceholder(); AnimateTo(1); }   // never held for a whole load
        Navigate(_pageUrl ?? CounterSheetRules.SheetUrl);
    }

    public void Post(int session, object msg)
    {
        if (session == 0 || session != _session) return;
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
            if (_live && _core != null) _core.PostWebMessageAsJson(json);
            else _pending.Add(json);
            if (_revealHeld && json.StartsWith("{\"type\":\"reply\"", StringComparison.Ordinal) && _reveal.Interval > RevealAfterReply) HoldReveal(RevealAfterReply);
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet.Post: {E}", ex.Message); }
    }

    public bool IsPageReady(int session) => session != 0 && session == _session && _live;

    /// <summary>The player is looking at this open sheet: its door is the foreground window.</summary>
    public bool IsForeground(int session)
    {
        if (session == 0 || session != _session || !DoorWindow.IsVisible) return false;
        try
        {
            var own = new WindowInteropHelper(DoorWindow).Handle;
            return own != IntPtr.Zero && GetForegroundWindow() == own;
        }
        catch (Exception ex) { Diag.Swallowed(ex); return false; }
    }

    public void FocusPage(int session)
    {
        if (session == 0 || session != _session) return;
        try
        {
            if (!DoorWindow.IsActive) DoorWindow.Activate();
            _web?.Focus();
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Uri) || !e.Uri.StartsWith("https://ccp.game/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) => e.Handled = true;

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (_pageUrl == null || !ChaosWebViewHost.SameDocument(e.Source, _pageUrl)) return;
            var json = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(json)) return;
            var o = JObject.Parse(json);
            switch ((string?)o["type"])
            {
                case "ready":
                    _pageReady = true;
                    if (_resetting) { _resetting = false; _resetWatch.Stop(); }
                    if (!_fresh) HandOver(_session);
                    break;
                case "log":
                    App.Logger?.Write(ChaosWebViewHost.PageLogLevel((string?)o["level"]), "PrizeSheet[page]: {Msg}", (string?)o["msg"]);
                    break;
                default:
                    if (_live) _onMessage?.Invoke(o);
                    break;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet.OnWebMessageReceived: {E}", ex.Message); }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        App.Logger?.Warning("PrizeSheet: WebView2 process failed ({Kind})", e.ProcessFailedKind);
        // Never inside the browser's own callback: the next open builds a new one.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            DisposeWeb();
            _wantsClose?.Invoke(WhyFailed);
        }));
    }

    // ============================== open and close ==============================

    /// <summary>Slide the sheet down. Returns this open's session (0 = cannot open: no room under the
    /// bar, the window is hidden, or there is no WebView2 runtime). <paramref name="wantsClose"/> hears
    /// the scrim, the door hiding or closing, no room, and a dead page.</summary>
    public int Open(Action<string> wantsClose)
    {
        if (_disposed || _noRuntime || !DoorWindow.IsVisible || !PlaceSlot()) return 0;
        EnsureWeb();
        if (_web == null) return 0;
        _openWatch.Restart();
        _slideAtMs = -1;
        _session = ++_sessions;
        _wantsClose = wantsClose;
        _down = true;
        _how = _core == null ? "cold" : _fresh ? (_pageReady ? "kept" : _resetting ? "resetting" : "loading") : _pageReady && _used ? "closing" : "loading";
        // A warm page waits for its init (Load hands it over a moment later): starting the reveal now
        // would run ahead of the page's own slide and show its bare ground first. Anything else slides at
        // once on the waiting card, the page's HWND cut out until it has painted.
        _openOnHandOver = CounterSheetRules.HoldsSlide(_fresh, _pageReady, _resetting, _core != null, _p);
        _waiting = !_openOnHandOver;
        if (_waiting) ShowPlaceholder();
        _region = (-1, -1, -1, -1);
        _slot.Visibility = Visibility.Visible;
        _scrim.Visibility = Visibility.Visible;
        _scrim.IsHitTestVisible = true;
        ShowEdge(false);
        ApplyProgress();
        RaiseHwnd();
        HideCards();
        _cardWatch.Start();
        try { _pressChip?.Invoke(true); } catch (Exception ex) { Diag.Swallowed(ex); }
        if (!_openOnHandOver) AnimateTo(1);
        return _session;
    }

    /// <summary>Close this open: slide up (or cut), then keep the page for the next open (<see cref="KeepPage"/>).</summary>
    public void Close(int session, bool animate)
    {
        if (session == 0 || session != _session) return;
        _session = 0;
        _live = false;
        _onReady = null;
        _onMessage = null;
        _wantsClose = null;
        _pending.Clear();
        _openOnHandOver = false;
        _revealHeld = false;
        _reveal.Stop();
        _scrim.IsHitTestVisible = false;
        ShowEdge(false);
        try { _pressChip?.Invoke(false); } catch (Exception ex) { Diag.Swallowed(ex); }
        if (animate) AnimateTo(0);
        else Finish(0);
    }

    private void AnimateTo(double to, int leadMs = 0)
    {
        if (to > 0 && _slideAtMs < 0 && _openWatch.IsRunning) _slideAtMs = _openWatch.ElapsedMilliseconds;
        _leadMs = leadMs;
        _from0 = _p;
        _scrim0 = _scrimP;
        _to = to;
        var level = Services.MotionFx.Level;
        bool gentle = Services.Safety.GentleVisuals.On;
        double span = Math.Abs(to - _p);
        _slideMs = (int)Math.Round(CounterSheetRules.SlideDurationMs(level, gentle) * span * SlowMo);
        _scrimMs = (int)Math.Round(CounterSheetRules.ScrimMs(level, gentle) * Math.Max(span, Math.Abs(to - _scrimP)) * SlowMo);
        if (_slideMs <= 0 && _scrimMs <= 0) { Finish(to); return; }
        _watch.Restart();
        _clock.Start();
        Step();
    }

    private void Step()
    {
        double ms = Math.Max(0, _watch.Elapsed.TotalMilliseconds - _leadMs);
        _p = _slideMs <= 0 ? _to : _from0 + (_to - _from0) * CounterSheetRules.EaseOut(ms / _slideMs);
        // Opening from closed: a soft overshoot and settle (Full; half at Reduced; none for Gentle or Off).
        _settleOff = _to > 0 && _from0 <= 0 && _slideMs > 0
            ? CounterSheetRules.OpenOffset(ms / _slideMs, _slot.ActualHeight,
                CounterSheetRules.OvershootDip(Services.MotionFx.Level, Services.Safety.GentleVisuals.On, _slot.ActualHeight))
            : double.NaN;
        _scrimP = _scrimMs <= 0 ? _to : _scrim0 + (_to - _scrim0) * Math.Clamp(ms / _scrimMs, 0, 1);
        ApplyProgress();
        if (ms >= Math.Max(_slideMs, _scrimMs)) Finish(_to);
    }

    private void Finish(double to)
    {
        _clock.Stop();
        _p = _scrimP = to;
        _settleOff = double.NaN;
        if (to > 0) { ApplyProgress(); ShowEdge(true); PostWalletAt(); return; }
        ApplyProgress();   // an empty band: nothing of the HWND shows (the slot stays visible: the browser keeps painting)
        _down = false;
        _cardWatch.Stop();
        if (_web == null) _slot.Visibility = Visibility.Collapsed;
        _scrim.Visibility = Visibility.Collapsed;
        _shadow.Visibility = Visibility.Collapsed;
        try { _pressChip?.Invoke(false); } catch (Exception ex) { Diag.Swallowed(ex); }
        RestoreCards();
        try { if (FocusDoorOnClose && DoorWindow.IsActive) DoorWindow.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
        // The next open finds the same page waiting for its init: no load between opens.
        _waiting = false;
        HidePlaceholder();
        KeepPage();
        try { Retracted?.Invoke(); } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    /// <summary>Paint one moment of the slide: the page comes down from under the bar (layout margin, a
    /// move, never a resize), its HWND is cut at the bar line, the scrim fades.</summary>
    private void ApplyProgress()
    {
        double h = _slot.ActualHeight;
        double off = double.IsNaN(_settleOff) ? CounterSheetRules.SlideOffset(_p, h) : _settleOff;
        double inset = CounterSheetRules.BorderDip + CounterSheetRules.PaddingDip;
        double top = CounterSheetRules.GapUnderBarDip + inset - off;
        _scrim.Opacity = _scrimP;
        if (_web == null) return;
        var margin = new Thickness(inset, top, inset, inset + off);
        if (_web.Margin != margin) _web.Margin = margin;
        if (_placeholder.Visibility == Visibility.Visible && _placeholder.Margin != margin) _placeholder.Margin = margin;
        // Whatever of it is still above the bar line is cut; all of it while this open has not painted (the waiting card shows).
        ApplyRegion(_waiting ? 1e6 : Math.Max(0, -top));
    }

    /// <summary>The gold edge: IN once the page has landed (a short fade, Gentle's floor), OUT at once.</summary>
    private void ShowEdge(bool on)
    {
        int ms = on ? CounterSheetRules.EdgeFadeMs(Services.MotionFx.Level, Services.Safety.GentleVisuals.On) : 0;
        foreach (var part in new UIElement[] { _frame, _shadow })
        {
            part.BeginAnimation(OpacityProperty, null);
            if (ms <= 0) { part.Opacity = on ? 1 : 0; continue; }
            part.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)));
        }
    }

    /// <summary>Shape the page's HWND: rounded like the frame's inside (no square corner past the curve),
    /// minus the top <paramref name="cutDip"/> that is still above the bar line.</summary>
    private void ApplyRegion(double cutDip)
    {
        try
        {
            var hwnd = _web?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            var m = PresentationSource.FromVisual(_web!)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            int w = (int)Math.Ceiling(_web!.ActualWidth * m.M11), h = (int)Math.Ceiling(_web.ActualHeight * m.M22);
            int top = Math.Min(h, (int)Math.Round(cutDip * m.M22));
            var key = (w, h, top, 0);
            if (key == _region) return;
            int r = 2 * (int)Math.Round(CounterSheetRules.InnerRadiusDip * m.M11);
            IntPtr band = CreateRectRgn(0, top, w, h);
            IntPtr shape = CreateRoundRectRgn(0, 0, w + 1, h + 1, r, r);
            CombineRgn(band, band, shape, RGN_AND);
            DeleteObject(shape);
            if (SetWindowRgn(hwnd, band, true) == 0) DeleteObject(band);   // on success the window owns it
            _region = key;
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet: region failed: {E}", ex.Message); }
    }

    /// <summary>Above any other child HWND in the window (the panel's browser, a video).</summary>
    private void RaiseHwnd()
    {
        try
        {
            var hwnd = _web?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    // ============================== the frame ==============================

    /// <summary>Place the sheet and the scrim under the bar for the window as it is now. False = no room.</summary>
    private bool PlaceSlot()
    {
        try
        {
            if (!_bar.IsVisible || PresentationSource.FromVisual(_bar) == null || PresentationSource.FromVisual(this) == null) return false;
            var tl = _bar.TranslatePoint(new Point(0, 0), this);
            var br = _bar.TranslatePoint(new Point(_bar.ActualWidth, _bar.ActualHeight), this);
            if (CounterSheetRules.Frame(new Size(ActualWidth, ActualHeight), new Rect(tl, br), CounterSheetRules.SideInsetDip,
                    CounterSheetRules.BottomInsetDip, CounterSheetRules.MinWidthDip, CounterSheetRules.MinHeightDip) is not { } f) return false;
            var slot = new Thickness(f.Left, f.Top, ActualWidth - f.Right, ActualHeight - f.Bottom);
            if (_slot.Margin != slot) _slot.Margin = slot;
            // The scrim covers the window's whole content from the bar's top (the panel's rail and header too),
            // with a hole where the wallet chip is: the chip reads as the door, and a press on it is the second press.
            var scrim = new Thickness(0, _walletChip == null ? f.Top : Math.Max(0, tl.Y), 0, 0);   // a game sheet's strip dims itself
            if (_scrim.Margin != scrim) _scrim.Margin = scrim;
            var chip = _walletChip?.Invoke() is { IsVisible: true } c && PresentationSource.FromVisual(c) != null
                ? new Rect(c.TranslatePoint(new Point(0, 0), this), c.TranslatePoint(new Point(c.ActualWidth, c.ActualHeight), this)) : Rect.Empty;
            var all = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight - scrim.Top)));
            _scrim.Clip = chip.IsEmpty ? null : new CombinedGeometry(GeometryCombineMode.Exclude, all,
                new RectangleGeometry(new Rect(chip.X - 3, chip.Y - scrim.Top - 3, chip.Width + 6, chip.Height + 6), 10, 10));
            double sd = CounterSheetRules.ShadowDip, gap = CounterSheetRules.GapUnderBarDip;
            _shadow.Margin = new Thickness(f.Left + sd, f.Top + gap + sd, Math.Max(0, ActualWidth - f.Right - sd), Math.Max(0, ActualHeight - f.Bottom - sd));
            _shadow.Visibility = Visibility.Visible;
            return true;
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet: place failed: {E}", ex.Message); return false; }
    }

    private void OnResized()
    {
        if (!_down) return;
        if (!PlaceSlot()) { _wantsClose?.Invoke("no-room"); return; }
        _region = (-1, -1, -1, -1);
        ApplyProgress();
        PostWalletAt();
    }

    /// <summary>The host's wallet star in the page's CSS pixels, measured against where the page sits once
    /// it is down (so it holds mid-slide too). Null: this door has no wallet star on screen (a game window).</summary>
    public (int X, int Y)? WalletAt()
    {
        try
        {
            if (WalletStarNow is not { IsVisible: true } star || PresentationSource.FromVisual(star) == null || PresentationSource.FromVisual(_slot) == null) return null;
            var c = star.TranslatePoint(new Point(star.ActualWidth / 2, star.ActualHeight / 2), _slot);
            double inset = CounterSheetRules.BorderDip + CounterSheetRules.PaddingDip;
            return SheetHostRules.WalletAt(c, inset, CounterSheetRules.GapUnderBarDip + inset);
        }
        catch (Exception ex) { Diag.Swallowed(ex); return null; }
    }

    /// <summary>The door draws Sparkles flights (the launcher, the panel: a wallet chip and a flight layer), so the page
    /// hands it its stars (init.walletFly). A game sheet has no chip.</summary>
    public bool HostFlies => _walletChip != null;

    /// <summary>The page's (0, 0) CSS pixel, once the sheet is down, in <paramref name="to"/>'s coordinates (the inverse of
    /// <see cref="WalletAt"/>). Null = not laid out.</summary>
    public Point? PageOrigin(Visual to)
    {
        try
        {
            if (PresentationSource.FromVisual(_slot) == null || PresentationSource.FromVisual(to) == null) return null;
            double inset = CounterSheetRules.BorderDip + CounterSheetRules.PaddingDip;
            var p = _slot.TransformToVisual(to).Transform(new Point(inset, CounterSheetRules.GapUnderBarDip + inset));
            return double.IsFinite(p.X) && double.IsFinite(p.Y) ? p : null;
        }
        catch (Exception ex) { Diag.Swallowed(ex); return null; }
    }

    /// <summary>Tell a live page where the wallet star moved to (the window was resized or the sheet landed).</summary>
    private void PostWalletAt()
    {
        if (!_live || _session == 0 || WalletAt() is not { } at || at == _walletSent) return;
        _walletSent = at;
        Post(_session, new { type = "wallet-at", x = at.X, y = at.Y });
    }

    private void OnDoorVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && !_down) { RestoreCards(); ScheduleWarm(); return; }   // cards held while the door was hidden
        if (e.NewValue is not false || !_down) return;
        var ask = _wantsClose;
        Finish(0);   // a hidden window cannot slide: cut
        ask?.Invoke("door-hidden");
    }

    private void OnDoorClosed(object? sender, EventArgs e)
    {
        var ask = _wantsClose;
        _disposed = true;
        _clock.Stop();
        _reveal.Stop();
        _cardWatch.Stop();
        _resetWatch.Stop();
        HidePlaceholder();
        _hiddenCards.Clear();
        DisposeWeb();
        ask?.Invoke("door-closed");
    }

    private void DisposeWeb()
    {
        var web = _web;
        _web = null;
        try
        {
            if (_core != null)
            {
                _core.NavigationStarting -= OnNavigationStarting;
                _core.NewWindowRequested -= OnNewWindowRequested;
                _core.WebMessageReceived -= OnWebMessageReceived;
                _core.ProcessFailed -= OnProcessFailed;
            }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
        _core = null;
        _pageUrl = null;
        _pageReady = _fresh = _live = _used = _resetting = false;
        _resetWatch.Stop();
        _region = (-1, -1, -1, -1);
        if (web == null) return;
        try { _slot.Children.Remove(web); web.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        if (!_down) _slot.Visibility = Visibility.Collapsed;
    }

    // ============================== the door's own windows and chip ==============================

    /// <summary>A wallet press while a modal holds the thread (a startup dialog, a message box): the sheet is
    /// in-tree and any window draws above it, so the press does not open it; the chip gives a small shake
    /// instead (<see cref="SheetHostRules.OnDoorPress"/>). True = the press is spent here.</summary>
    public bool NudgeIfBusy()
    {
        bool enabled = true;
        try { var h = new WindowInteropHelper(DoorWindow).Handle; enabled = h == IntPtr.Zero || IsWindowEnabled(h); }
        catch (Exception ex) { Diag.Swallowed(ex); }
        if (SheetHostRules.OnDoorPress(enabled, ComponentDispatcher.IsThreadModal) == SheetHostRules.DoorPress.Open) return false;
        App.Logger?.Information("PrizeSheet: a modal is up over {From}, the wallet only nudges", _from);
        double amp = SheetHostRules.NudgeDip(Services.MotionFx.Level, Services.Safety.GentleVisuals.On);
        if (amp > 0 && _walletChip?.Invoke() is { } chip && Face(chip) is { } t)
        {
            var shake = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(280), FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
            int i = 0;
            foreach (var x in new[] { 0, -amp, amp, -amp / 2, amp / 3, 0 })
                shake.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(x, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(56 * i++))));
            t.BeginAnimation(TranslateTransform.XProperty, shake);
        }
        return true;
    }

    /// <summary>The door's floating cards (a passive startup card, "What moved") are separate windows and
    /// would float over the sheet: hidden while it is down, shown again once it is up.</summary>
    private void HideCards()
    {
        try
        {
            // The door's own cards, and the passive cards nobody owns (a server announcement opens unowned).
            foreach (Window w in Application.Current.Windows)
                if ((ReferenceEquals(w.Owner, DoorWindow) || (w.Owner == null && w is Services.Startup.IPassiveStartupSurface))
                    && SheetHostRules.HidesWhileDown(w.IsVisible, w is Services.Startup.IPassiveStartupSurface || w is WhatMovedCard) && !_hiddenCards.Contains(w))
                {
                    w.Hide();
                    _hiddenCards.Add(w);
                }
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }

    private void RestoreCards()
    {
        if (_hiddenCards.Count == 0 || !DoorWindow.IsVisible || _down) return;
        var cards = _hiddenCards.ToArray();
        _hiddenCards.Clear();
        foreach (var w in cards)
            try { w.Show(); } catch (InvalidOperationException) { /* closed while hidden */ } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    /// <summary>ON IS PRESSED IN (Depth law): while its sheet is down the wallet chip sits in a well (house
    /// depth brushes, no Effect) and its face drops a pixel; it pops back as the sheet goes up.</summary>
    internal static void PressChip(FrameworkElement? chip, bool on)
    {
        if (chip == null) return;
        if (chip is SparkleWallet wallet) wallet.SetPressedIn(on);
        else if (chip is Control c)
        {
            if (on)
            {
                if (PressedFill(c, c.Background) is { } fill) c.Background = fill;
                c.SetResourceReference(Control.BorderBrushProperty, "DepthPressedBevel");
                c.BorderThickness = new Thickness(2);
            }
            else { c.ClearValue(Control.BackgroundProperty); c.ClearValue(Control.BorderBrushProperty); c.ClearValue(Control.BorderThicknessProperty); }
        }
        if (Face(chip) is { } t) t.Y = on ? 1 : 0;
    }

    /// <summary>The chip's own fill with the house <c>DepthPressedShade</c> laid over it (the shade falls from the
    /// top edge into the well), as one frozen brush; null when either is missing.</summary>
    internal static Brush? PressedFill(FrameworkElement at, Brush? basis)
    {
        if (basis == null || at.TryFindResource("DepthPressedShade") is not Brush shade) return null;
        var unit = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(basis, null, unit));
        group.Children.Add(new GeometryDrawing(shade, null, unit));
        var fill = new DrawingBrush(group) { Stretch = Stretch.Fill };
        if (fill.CanFreeze) fill.Freeze();
        return fill;
    }

    /// <summary>The chip's face (its content), with a translate of its own; null when something else owns its transform.</summary>
    private static TranslateTransform? Face(FrameworkElement chip)
    {
        var face = chip is ContentControl { Content: FrameworkElement f } ? f : chip;
        if (face.RenderTransform is TranslateTransform t) return t;
        if (face.RenderTransform is { } rt && !rt.Value.IsIdentity) return null;
        var made = new TranslateTransform();
        face.RenderTransform = made;
        return made;
    }

    // ============================== DEBUG desk hooks ==============================

#if DEBUG
    /// <summary>DEBUG: <c>CCP_SHEET_SLOWMO</c> stretches the slide (desk screenshots of the middle of it).</summary>
    private static double SlowMo
    {
        get
        {
            var v = System.Environment.GetEnvironmentVariable("CCP_SHEET_SLOWMO");
            return double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) && x is >= 1 and <= 20 ? x : 1;
        }
    }

    /// <summary>DEBUG: <c>CCP_SHEET_DEMO=launcher|wallet</c> presses that door's wallet four times (open, close,
    /// open, close) on a clock once warm, for a sandboxed desk run where the chip is hidden (signed out).</summary>
    private void DemoLater()
    {
        if (System.Environment.GetEnvironmentVariable("CCP_SHEET_DEMO") != _from) return;
        int step = 0;
        var t = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(4) };
        t.Tick += (_, _) =>
        {
            if (++step > 4 || _disposed) { t.Stop(); return; }
            App.Logger?.Information("PrizeSheet: demo press {Step} at {From}", step, _from);
            PrizeCounterHostService.Launch(from: _from);
        };
        t.Start();
    }
#else
    private const double SlowMo = 1;
#endif

    private const int RGN_AND = 1;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
}
