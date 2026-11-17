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
/// <para>The WebView2 is created once, a few seconds after the window is up, and kept hidden with the
/// page loaded and waiting for its <c>init</c>, so a first open is instant; after every close the page
/// is loaded fresh again. Both doors share one browser profile (<see cref="UserDataFolderName"/>) and
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

    private static readonly TimeSpan WarmDelay = TimeSpan.FromSeconds(4);

    /// <summary>sheet.css <c>--night</c>: the page's ground, so a slow first paint is never white.</summary>
    private static readonly Color Night = Color.FromRgb(0x13, 0x0D, 0x1E);

    /// <summary>sheet.css <c>--gold</c>, the wallet's gold: the sheet's edge.</summary>
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xD2, 0x4A);

    private static Task<CoreWebView2Environment>? _env;

    private readonly FrameworkElement _bar;
    private readonly string _from;
    private readonly Border _scrim;
    private readonly Grid _slot;
    private readonly Border _frame;
    private readonly FrameClock _clock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _watch = new();
    private WebView2? _web;
    private CoreWebView2? _core;
    private bool _disposed, _noRuntime;

    // The page: which document, whether it said ready, whether a counter owns it.
    private string? _pageUrl;
    private bool _pageReady, _fresh, _live;
    private int _session, _sessions;
    private Action? _onReady;
    private Action<JObject>? _onMessage;
    private Action<string>? _wantsClose;
    private readonly List<string> _pending = new();

    // The slide.
    private double _p, _from0, _to, _scrim0, _scrimP;
    private bool _openOnHandOver;
    private int _slideMs, _scrimMs, _leadMs;
    private (int, int, int, int) _region = (-1, -1, -1, -1);

    public Window DoorWindow { get; }

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
        _frame = new Border
        {
            Background = new SolidColorBrush(Night),
            BorderBrush = new SolidColorBrush(Gold),
            BorderThickness = new Thickness(CounterSheetRules.BorderDip),
            CornerRadius = new CornerRadius(CounterSheetRules.CornerRadiusDip),
            Margin = new Thickness(0, CounterSheetRules.GapUnderBarDip, 0, 0),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _slot = new Grid { ClipToBounds = true, Visibility = Visibility.Collapsed };
        _slot.Children.Add(_frame);
        Children.Add(_scrim);
        Children.Add(_slot);
        SizeChanged += (_, _) => OnResized();
        _clock.Tick += (_, _) => Step();
        door.IsVisibleChanged += OnDoorVisibleChanged;
        door.Closed += OnDoorClosed;
    }

    /// <summary>Lay a sheet over <paramref name="host"/> (the window's root grid) under <paramref name="bar"/>,
    /// and warm its page a few seconds after the window is up. <paramref name="insertAt"/> keeps the
    /// window's own top layers (veil, edge) above the scrim; -1 adds it on top with <paramref name="zIndex"/>.</summary>
    public static CounterSheetView Mount(Window door, Panel host, FrameworkElement bar, string from, int insertAt = -1, int zIndex = 0)
    {
        var view = new CounterSheetView(door, bar, from);
        if (zIndex != 0) Panel.SetZIndex(view, zIndex);
        if (insertAt >= 0 && insertAt <= host.Children.Count) host.Children.Insert(insertAt, view);
        else host.Children.Add(view);
        view.ScheduleWarm();
        return view;
    }

    // ============================== warm ==============================

    private void ScheduleWarm()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = WarmDelay };
        timer.Tick += (_, _) =>
        {
            if (_disposed) { timer.Stop(); return; }
            if (!DoorWindow.IsVisible) return;   // try again next tick, once the window shows
            timer.Stop();
            EnsureWeb();
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
            var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(Night.R, Night.G, Night.B) };
            web.SizeChanged += (_, _) => ApplyProgress();
            _web = web;
            _slot.Children.Add(web);
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
        if (_core == null) { _pageUrl = url; return; }   // InitCoreAsync navigates it
        if (_fresh && _pageUrl != null && ChaosWebViewHost.SameDocument(_pageUrl, url))
        {
            _fresh = false;
            if (_pageReady) Dispatcher.BeginInvoke(new Action(() => HandOver(session)));
            return;
        }
        Navigate(url);
    }

    private void HandOver(int session)
    {
        if (session == 0 || session != _session || !_pageReady || _live || _core == null) return;
        _live = true;
        foreach (var json in _pending)
            try { _core.PostWebMessageAsJson(json); } catch (Exception ex) { Diag.Swallowed(ex); }
        _pending.Clear();
        try { _onReady?.Invoke(); } catch (Exception ex) { Diag.Swallowed(ex); }
        // A warm page slides its content in the moment its init lands: the reveal starts with it.
        // Two frames of lead: the page paints, then starts its transition on the next animation frame.
        if (_openOnHandOver) { _openOnHandOver = false; AnimateTo(1, leadMs: 32); }
    }

    public void Post(int session, object msg)
    {
        if (session == 0 || session != _session) return;
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
            if (_live && _core != null) _core.PostWebMessageAsJson(json);
            else _pending.Add(json);
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
        _session = ++_sessions;
        _wantsClose = wantsClose;
        _slot.Visibility = Visibility.Visible;
        _scrim.Visibility = Visibility.Visible;
        _scrim.IsHitTestVisible = true;
        ShowEdge(false);
        ApplyProgress();
        RaiseHwnd();
        // A warm page waits for its init (Load hands it over a moment later): starting the reveal now
        // would run ahead of the page's own slide and show its bare ground first.
        _openOnHandOver = _fresh && _pageReady && _core != null && _p <= 0;
        if (!_openOnHandOver) AnimateTo(1);
        return _session;
    }

    /// <summary>Close this open: slide up (or cut), then load the page fresh for the next open.</summary>
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
        _scrim.IsHitTestVisible = false;
        ShowEdge(false);
        if (animate) AnimateTo(0);
        else Finish(0);
    }

    private void AnimateTo(double to, int leadMs = 0)
    {
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
        _scrimP = _scrimMs <= 0 ? _to : _scrim0 + (_to - _scrim0) * Math.Clamp(ms / _scrimMs, 0, 1);
        ApplyProgress();
        if (ms >= Math.Max(_slideMs, _scrimMs)) Finish(_to);
    }

    private void Finish(double to)
    {
        _clock.Stop();
        _p = _scrimP = to;
        if (to > 0) { ApplyProgress(); ShowEdge(true); return; }
        ApplyProgress();   // an empty band: nothing of the HWND shows
        _slot.Visibility = Visibility.Collapsed;
        _scrim.Visibility = Visibility.Collapsed;
        try { if (DoorWindow.IsActive) DoorWindow.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
        // The next open finds a page loaded and waiting for its init.
        if (_core != null && _session == 0) Navigate(CounterSheetRules.SheetUrl);
    }

    /// <summary>Paint one moment of the slide: the page comes down from under the bar (layout margin, a
    /// move, never a resize), its HWND is cut at the bar line, the scrim fades.</summary>
    private void ApplyProgress()
    {
        double h = _slot.ActualHeight;
        double off = CounterSheetRules.SlideOffset(_p, h);
        double inset = CounterSheetRules.BorderDip + CounterSheetRules.PaddingDip;
        double top = CounterSheetRules.GapUnderBarDip + inset - off;
        _scrim.Opacity = _scrimP;
        if (_web == null) return;
        var margin = new Thickness(inset, top, inset, inset + off);
        if (_web.Margin != margin) _web.Margin = margin;
        ApplyRegion(Math.Max(0, -top));   // whatever of it is still above the bar line is cut
    }

    /// <summary>The gold edge: IN once the page has landed (a short fade, Gentle's floor), OUT at once.</summary>
    private void ShowEdge(bool on)
    {
        _frame.BeginAnimation(OpacityProperty, null);
        int ms = on ? CounterSheetRules.EdgeFadeMs(Services.MotionFx.Level, Services.Safety.GentleVisuals.On) : 0;
        if (ms <= 0) { _frame.Opacity = on ? 1 : 0; return; }
        _frame.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)));
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
            var scrim = new Thickness(0, f.Top, 0, 0);
            if (_scrim.Margin != scrim) _scrim.Margin = scrim;
            return true;
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet: place failed: {E}", ex.Message); return false; }
    }

    private void OnResized()
    {
        if (_slot.Visibility != Visibility.Visible) return;
        if (!PlaceSlot()) { _wantsClose?.Invoke("no-room"); return; }
        _region = (-1, -1, -1, -1);
        ApplyProgress();
    }

    private void OnDoorVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not false || _slot.Visibility != Visibility.Visible) return;
        var ask = _wantsClose;
        Finish(0);   // a hidden window cannot slide: cut
        ask?.Invoke("door-hidden");
    }

    private void OnDoorClosed(object? sender, EventArgs e)
    {
        var ask = _wantsClose;
        _disposed = true;
        _clock.Stop();
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
        _pageReady = _fresh = _live = false;
        _region = (-1, -1, -1, -1);
        if (web == null) return;
        try { _slot.Children.Remove(web); web.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
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
}
