// PORTED from WPF 7.1.5 Controls/Leash/LeashPunishWindow.cs: the window a leash video plays in (92% of the work area,
// F11 fullscreen), WPF's cage script over the video. A PUNISHMENT refuses a close (shake + whose); it ends on the
// video's end/cap (the runner), a cut, or a panic (LeashTaskHost.OnPanicPress -> CloseNow). A TASK closes normally.
// Head differences (WebHost = NativeWebView): the cage runs after each load and each second (no document-created
// injection); messages ride the engine's invokeCSharpAction string carrier (same 'leash-ended'/'leash-error'); a local
// file plays from a page written under the user data; no HTTP status, so only no engine or a media error fail it.
// ponytail: the refusal's LeashFx.Denied sound lands with the leash fx unit.
using System;
using System.IO;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashPunishWindow : Window
{
    public static LeashPunishWindow? Current { get; private set; }
    /// <summary>The page's video reached its end.</summary>
    public static event Action? VideoEnded;
    /// <summary>The page could not load or the local video would not decode.</summary>
    public static event Action? PlaybackFailed;
    /// <summary>Test seam: false builds the window without a web view (headless tests).</summary>
    internal static bool CreateWeb { get; set; } = true;
    /// <summary>The head's Cut leash: the panel's cut (LeashCutSafety first, whatever the network does).</summary>
    internal static Action CutLeash { get; set; } = () => _ = Windows.MainShellWindow.Current?.CutLeashAsync();
    private readonly bool _locked;
    private readonly string _holder;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;
    private readonly Grid _root;
    private WebHost? _web;
    private bool _failed;
    private bool _allowClose;
    private string? _pageUrl;
    private WindowState _prevState;
    private DispatcherTimer? _refuseTimer;
    private Helpers.BeatLoop? _shake;
    private DispatcherTimer? _cageTimer;
    internal WebHost? View => _web;
    internal string TitleLine => _title.Text ?? "";
    internal bool Locked => _locked;
    /// <summary>True while the window is up and not minimised (only then does watching count).</summary>
    public static bool Watchable => Current is { IsVisible: true } w && w.WindowState != WindowState.Minimized;
    internal LeashPunishWindow() : this(locked: true, "Vex") { }   // --render-view sample
    private LeashPunishWindow(bool locked, string holder)
    {
        _locked = locked;
        _holder = string.IsNullOrWhiteSpace(holder) ? "?" : holder.Trim();
        Title = TitleText();
        Background = Brushes.Black;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        (Width, Height) = (1280, 760);
        try
        {
            if (Screens.Primary is { } s)
            {
                Width = Math.Max(640, s.WorkingArea.Width / s.Scaling * 0.92);
                Height = Math.Max(400, s.WorkingArea.Height / s.Scaling * 0.92);
            }
        }
        catch { }
        _root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(14, 6, 8, 6) };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _title = F.Label(TitleText(), 13.5, F.Text, F.Display, FontWeight.SemiBold);
        _title.Tag = "leash-punish-title";
        words.Children.Add(_title);
        _hint = F.Label(HintText(), 11.5, F.Muted);
        _hint.Tag = "leash-punish-hint";
        _hint.Margin = new Thickness(0, 1, 0, 0);
        words.Children.Add(_hint);
        head.Children.Add(words);
        var buttonBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33));
        var cut = HeadButton(IconKind.Cut, Loc.Get("leash_cut"), F.Mint, buttonBg, "leash-punish-cut", F.Mint);
        ToolTip.SetTip(cut, Loc.Get("leash_cut_tip"));
        cut.Margin = new Thickness(8, 0, 6, 0);
        cut.Click += (_, _) => CutFromWindow();
        Grid.SetColumn(cut, 1);
        head.Children.Add(cut);
        var fs = HeadButton(IconKind.FullScreenMaximize, Loc.Get("leash_punish_fullscreen"), F.Text, buttonBg, "leash-punish-fullscreen", F.Line2);
        ToolTip.SetTip(fs, Loc.Get("leash_punish_fullscreen_key"));
        fs.Click += (_, _) => ToggleFullscreen();
        Grid.SetColumn(fs, 2);
        head.Children.Add(fs);
        // WPF FriendsLook.HeadBrush: Raised to Panel, top to bottom.
        _root.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0x2C, 0x14, 0x50), 0), new GradientStop(Color.FromRgb(0x1A, 0x0E, 0x2E), 1) },
            },
            Child = head,
            Tag = "leash-punish-head",
        });
        Content = _root;
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private static Button HeadButton(IconKind icon, string text, IBrush fg, IBrush bg, string tag, IBrush border)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var g = IconGlyph.Of(icon, IconVariant.Regular, null, 13);
        (g.Foreground, g.Margin) = (fg, new Thickness(0, 0, 5, 0));
        sp.Children.Add(g);
        sp.Children.Add(F.Label(text, 12, fg, F.Display));
        var b = F.Pill(sp, bg, fg, tag, border);
        // Never takes focus from the page: Space/Enter must not reach it while the video plays (WPF Focusable=false).
        (b.Focusable, b.Padding, b.Cursor) = (false, new Thickness(10, 3, 10, 3), F.Hand());
        return b;
    }
    private string TitleText() => Loc.GetF(_locked ? "leash_punish_title" : "leash_task_title", _holder);
    // WPF says "hold {key} 5 s to cut"; hold-to-cut is not on this head yet, so the hint names the Cut button.
    private string HintText() => Loc.Get(_locked ? "leash_ask_p_cut" : "leash_task_close_hint");
    /// <summary>The head's Cut leash. The window goes first so nothing is left caged while the service works.</summary>
    internal void CutFromWindow()
    {
        Serilog.Log.Information("Leash: cut from the video window");
        CloseNow();
        try { CutLeash(); } catch (Exception ex) { Serilog.Log.Warning("Leash cut from window failed: {E}", ex.Message); }
    }
    // ---- open / close ------------------------------------------------------------------------
    /// <summary>Open a web page (a Hypnotube video) in the cage. Replaces any open one. https only.</summary>
    public static bool OpenUrl(string url, string holder, bool locked)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        return Open(u, holder, locked);
    }
    private static bool Open(Uri page, string holder, bool locked)
    {
        var w = Show(holder, locked);
        w._pageUrl = page.AbsoluteUri;
        w.Load(page);
        return true;
    }
    /// <summary>Open a local video file on a plain page in the cage.</summary>
    public static bool OpenFile(string path, string holder, bool locked)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(CorePaths.UserData, "leash_video")).FullName;
            var file = Path.Combine(dir, "index.html");
            File.WriteAllText(file, LocalPage(new Uri(Path.GetFullPath(path)).AbsoluteUri));
            return Open(new Uri(file), holder, locked);
        }
        catch (Exception ex) { Serilog.Log.Warning("Leash video page could not be written: {E}", ex.Message); return false; }
    }
    /// <summary>Close without a fuss: the video is done, the leash is off, or panic.</summary>
    public static void CloseNow()
    {
        var w = Current;
        if (w == null) return;
        w._allowClose = true;
        try { w.Close(); } catch { }
        if (ReferenceEquals(Current, w)) Current = null;
    }
    private static LeashPunishWindow Show(string holder, bool locked)
    {
        CloseNow();
        var w = new LeashPunishWindow(locked, holder);
        Current = w;
        w.Show();
        try { w.Activate(); } catch { }
        return w;
    }
    /// <summary>The page -> host carrier: the engine's string carrier, else WebView2's postMessage.</summary>
    private const string Post =
        "var __leashPost=function(m){try{if(window.invokeCSharpAction)window.invokeCSharpAction(m);else chrome.webview.postMessage(m);}catch(e){}};";
    internal static string LocalPage(string src) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><script>" + Post + "</script>"
        + "<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}"
        + "video{width:100vw;height:100vh;object-fit:contain;background:#000}</style></head><body>"
        + "<video autoplay playsinline onerror=\"__leashPost('leash-error')\" src=\""
        + WebUtility.HtmlEncode(src) + "\"></video></body></html>";
    // ---- the browser -------------------------------------------------------------------------
    private void Load(Uri page)
    {
        if (!CreateWeb) return;
        try
        {
            _web = new WebHost { AllowNavigation = Allowed };
            Grid.SetRow(_web, 1);
            _root.Children.Add(_web);
            if (!_web.HasEngine) { Fail("no web engine"); return; }
            _web.WebMessage += OnMessage;
            _web.NavigationCompleted += u => _ = InjectCageAsync();
            _cageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cageTimer.Tick += (s, e) => _ = InjectCageAsync();
            _cageTimer.Start();
            _web.Navigate(page);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Leash video window failed: {E}", ex.Message);
            Fail("web view failed");
        }
    }
    /// <summary>WPF WebMessageReceived: the same two messages (a carrier may JSON-quote the string).</summary>
    internal void OnMessage(string raw)
    {
        var msg = raw.Trim().Trim('"');
        if (msg == "leash-ended")
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(Current, this)) VideoEnded?.Invoke(); });
        else if (msg == "leash-error") Fail("media error");
    }
    private async System.Threading.Tasks.Task InjectCageAsync()
    {
        try { if (_web is { } w && !_allowClose) await w.InvokeScriptAsync(CageScript); }
        catch { }
    }
    /// <summary>The first load goes through; after it, only the same page (a reload) may load.</summary>
    internal bool Allowed(Uri target) => _pageUrl == null || SamePage(_pageUrl, target.AbsoluteUri);
    /// <summary>WPF AppLeashTaskHost.SamePage: same host (www. ignored) and path, query ignored.</summary>
    internal static bool SamePage(string? pageUrl, string? targetUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)) return false;
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var target)) return false;
        static string Host(Uri u) => u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        return string.Equals(Host(page), Host(target), StringComparison.OrdinalIgnoreCase)
            && string.Equals(page.AbsolutePath.TrimEnd('/'), target.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>Once per window: the video cannot play here.</summary>
    private void Fail(string why)
    {
        if (_failed || _allowClose) return;
        _failed = true;
        Serilog.Log.Information("Leash video window: will not play ({Why})", why);
        // Posted: the handler closes this window, which must not happen inside a web view callback.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(Current, this)) return;
            try { PlaybackFailed?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("Leash playback-failed handler failed: {E}", ex.Message); }
        });
    }
    // ---- fullscreen, close, shake ------------------------------------------------------------
    internal void ToggleFullscreen()
    {
        if (WindowState != WindowState.FullScreen) (_prevState, WindowState) = (WindowState, WindowState.FullScreen);
        else WindowState = _prevState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // A punishment refuses a close, except when the app or the OS is shutting down (WPF SessionEnding).
        if (_locked && !_allowClose && e.CloseReason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown))
        {
            e.Cancel = true;
            Refuse();
            return;
        }
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (ReferenceEquals(Current, this)) Current = null;
        _refuseTimer?.Stop();
        _shake?.Stop();
        _cageTimer?.Stop();
        if (_web != null) _web.WebMessage -= OnMessage;
        _web = null;
    }
    /// <summary>A close on a punishment: the window shakes and the head says whose it is, for a few seconds.</summary>
    internal void Refuse()
    {
        _title.Text = Loc.GetF("leash_punish_no_close", _holder);
        (_title.Foreground, _hint.Foreground) = (F.Gold, F.Gold);
        _refuseTimer?.Stop();
        _refuseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refuseTimer.Tick += (_, _) =>
        {
            _refuseTimer?.Stop();
            _title.Text = TitleText();
            (_title.Foreground, _hint.Foreground) = (F.Text, F.Muted);
        };
        _refuseTimer.Start();
        var level = CoreSettings.Current?.MotionLevel ?? Models.MotionLevel.Full;
        if (level != Models.MotionLevel.Off) Shake(level == Models.MotionLevel.Reduced ? 7.0 : 16.0);
    }
    /// <summary>Moves the real window (a native web view does not follow a render transform; P82) and lands
    /// back exactly where it was.</summary>
    private void Shake(double amp)
    {
        if (WindowState is WindowState.Maximized or WindowState.FullScreen) return;   // Position is ignored there
        var origin = Position;
        _shake?.Stop();
        const double ms = 420;
        // On the shared beat (AmbientBeatLoopsTests ratchet), not a private 16 ms timer.
        _shake = new Helpers.BeatLoop(this, sec =>
        {
            var t = sec * 1000;
            var dx = t >= ms ? 0 : (int)Math.Round(amp * (1 - t / ms) * Math.Sin(t / ms * Math.PI * 9));
            try { Position = new PixelPoint(origin.X + dx, origin.Y); } catch { }
            if (t >= ms) _shake?.Stop();
        });
        _shake.Start();
    }
    /// <summary>WPF CageScript, whitespace-packed (same statements); only the message carrier is this head's (<see cref="Post"/>). Idempotent.</summary>
    internal const string CageScript = @"(function(){
  if (window.__leashCage) return; window.__leashCage = 1; " + Post + @"
  var stop = function(e){ e.preventDefault(); e.stopImmediatePropagation(); }; try {
  var no = function(){ return Promise.resolve(); }; Element.prototype.requestFullscreen = no;
  Element.prototype.webkitRequestFullscreen = no; HTMLVideoElement.prototype.webkitEnterFullscreen = function(){};
  HTMLVideoElement.prototype.requestPictureInPicture = function(){ return Promise.reject(); }; } catch (e) {}
  ['keydown','keyup','keypress','contextmenu','dblclick','auxclick','wheel'].forEach(function(t){ window.addEventListener(t, stop, true); });
  var css = 'html,body{overflow:hidden!important;background:#000!important}'
  + 'video.leash-cage{position:fixed!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;'
  + 'max-width:none!important;max-height:none!important;margin:0!important;transform:none!important;'
  + 'z-index:2147483646!important;background:#000!important;object-fit:contain!important;visibility:visible!important;opacity:1!important}'
  + '#leash-shield{position:fixed;left:0;top:0;width:100vw;height:100vh;z-index:2147483647;background:transparent;cursor:none}';
  var style = document.createElement('style'); style.textContent = css; var v = null, ended = false;
  var pick = function(){ var best = null, score = -1; document.querySelectorAll('video').forEach(function(x){
  var r = x.getBoundingClientRect(); var s = (r.width * r.height) + (isFinite(x.duration) ? x.duration * 1000 : 0);
  if (s > score) { best = x; score = s; } }); return best; }; var free = function(el){
  for (var p = el.parentElement; p && p !== document.documentElement; p = p.parentElement) {
  p.style.setProperty('transform', 'none', 'important'); p.style.setProperty('filter', 'none', 'important');
  p.style.setProperty('contain', 'none', 'important'); p.style.setProperty('perspective', 'none', 'important');
  p.style.setProperty('z-index', '2147483646', 'important'); } }; var tick = function(){
  var host = document.head || document.documentElement; if (host && !style.isConnected) host.appendChild(style);
  if (document.body && !document.getElementById('leash-shield')) {
  var s = document.createElement('div'); s.id = 'leash-shield';
  ['mousedown','mouseup','click','pointerdown','pointerup','touchstart','touchend'].forEach(function(t){ s.addEventListener(t, stop, true); });
  document.body.appendChild(s); } var nv = pick(); if (nv && nv !== v) { v = nv;
  v.addEventListener('ended', function(){ ended = true; __leashPost('leash-ended'); }); } if (v) {
  v.classList.add('leash-cage'); free(v); v.controls = false; v.loop = false; if (v.muted) v.muted = false;
  if (v.paused && !ended) { var p = v.play(); if (p && p.catch) p.catch(function(){}); } }
  if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(function(){}); }; try {
  ['pause','stop','seekbackward','seekforward','seekto','previoustrack','nexttrack'].forEach(function(a){
  try { navigator.mediaSession.setActionHandler(a, function(){ if (v && !ended) v.play(); }); } catch (e) {} });
  } catch (e) {} document.addEventListener('DOMContentLoaded', tick); tick(); setInterval(tick, 400);
})();";
}
