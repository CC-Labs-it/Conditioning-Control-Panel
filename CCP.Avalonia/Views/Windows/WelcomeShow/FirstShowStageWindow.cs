using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.FirstShow;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowDesktopWindow.cs: EMI and the show's
/// effects over the primary screen. The stage is transparent and click-through (<see cref="X11Overlay"/>); EMI is
/// the real <see cref="EmiDeskWindow"/> presenting; her speech card is its own window so its buttons stay
/// clickable (WPF :30). Timings are <see cref="FirstShowScript"/>'s; the guide after the verdict is the Guide
/// partial. Live bubble circles take clicks until gather; the rest of the stage stays passive.</summary>
internal sealed partial class FirstShowStageWindow : Window
{
    private readonly Canvas _stage = new() { Background = null };
    /// <summary>WPF FirstShowDesktopWindow.cs:50: "via Scrolller" during a show of fetched (Scrolller) pictures.</summary>
    internal readonly ScrolllerCreditBadge Credit = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private EmiDeskWindow? _emi;
    private readonly Border _bubble = new()
    {
        Width = 390, CornerRadius = new CornerRadius(18), Padding = new Thickness(18),
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 50)), BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(1)
    };
    private readonly Window _speechWindow = new()
    {
        Title = "CCP - Emi speech", WindowDecorations = WindowDecorations.None, Background = Brushes.Transparent,
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }, CanResize = false, ShowInTaskbar = false,
        ShowActivated = false, Topmost = true, SizeToContent = SizeToContent.Manual, WindowStartupLocation = WindowStartupLocation.Manual
    };
    private readonly StackPanel _bubbleContent = new();
    private readonly TextBlock _speech = new() { Foreground = Brushes.White, FontSize = 17, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _loadingPanel = new() { IsVisible = false, Margin = new Thickness(0, 14, 0, 0) };
    private readonly ProgressBar _loadingBar = new() { Height = 9, MinHeight = 9, Minimum = 0, Maximum = 6, Foreground = Brushes.HotPink, Background = new SolidColorBrush(Color.FromRgb(55, 38, 70)) };
    private readonly TextBlock _loadingStatus = new() { Foreground = Brushes.LightGray, FontSize = 13, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _choices = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Border _logo = new() { Width = 460, CornerRadius = new CornerRadius(22), Padding = new Thickness(5), BorderThickness = new Thickness(3), Opacity = 0, IsHitTestVisible = false };
    private readonly FirstShowAudio _audio = new();
    private readonly FrameClock _clock;
    private readonly Stopwatch _life = Stopwatch.StartNew();
    private readonly List<string> _files = new();
    private readonly string _panic;
    private readonly MainShellWindow? _main;
    private PixelRect _screenPx;
    private double _scale;
    private readonly bool _redirected;
    private FirstShowEffects? _effects;
    private CancellationTokenSource? _load;
    internal Func<(PixelPoint At, bool Pressed, bool Escape)?> ReadDesktopInput = X11Pointer.Read;
    private PixelRect[] _inputBuf = new PixelRect[64];   // reused every frame
    internal int InputRectCount { get; private set; }
    internal int InputPushes { get; private set; }
    internal PixelRect[] InputRects => _inputBuf[..InputRectCount];
    internal FirstShowEffects? Effects => _effects;
    private bool _closed, _greeted, _centered, _playing, _ending, _serious, _moving, _bundled;
    private double _last, _moveAt, _showStart = double.NaN, _showFrozen, _settledAt, _x, _y;
    private int _beat = -1;
    private static readonly HttpClient Http = new(UrlSafety.CreateGuardedHandler()) { Timeout = TimeSpan.FromSeconds(20) };
    /// <summary>"via Scrolller" while a Scrolller show plays (WPF FirstShowDesktopWindow._credit, ScrolllerCreditBadge).
    /// Brand text, not a loc key, as on every page's mark.</summary>
    private readonly Border _credit = new()
    {
        IsVisible = false, IsHitTestVisible = false, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 2),
        Background = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)),
        Child = new TextBlock { Text = ScrolllerCreditText, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.Medium },
    };
    internal const string ScrolllerCreditText = "via Scrolller";
    /// <summary>The credit is up (tests read it).</summary>
    internal bool CreditShown => Credit.Shown;   // one badge (Mich's ScrolllerCreditBadge); _credit is unused
    /// <summary>Seconds since the stage was built. Tests step it (P08).</summary>
    internal Func<double> Clock;
    /// <summary>Test seams: the feed page for (preset, attempt), and the bundled pictures.</summary>
    internal Func<string, int, CancellationToken, Task<IReadOnlyList<FypAssetManifest.Entry>>> FetchFeed = DefaultFetch;
    internal Func<string, CancellationToken, Task<string?>> Download = DownloadAsync;
    internal Func<IReadOnlyList<string>> BundledPictures = () => Enumerable.Range(0, 4)
        .Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", "gif" + i + ".webp")))
        .Where(File.Exists).ToArray();
    internal static string Text(string key) => Loc.Get("first_show_" + key);
    internal string SpeechText => _speech.Text ?? string.Concat(_speech.Inlines?.OfType<Run>().Select(r => r.Text) ?? Array.Empty<string>());
    internal IReadOnlyList<Button> ChoiceButtons => _choices.Children.OfType<Button>().ToArray();
    internal bool Playing => _playing;
    internal FirstShowAudio Audio => _audio;
    internal double LogoOpacity => _logo.Opacity;
    /// <summary>--render-all: the stage at the reveal (logo up), nothing else on it.</summary>
    internal FirstShowStageWindow() : this(null) => _logo.Opacity = 1;
    public FirstShowStageWindow(MainShellWindow? main)
    {
        Clock = () => _life.Elapsed.TotalSeconds;
        _main = main;
        _panic = CoreSettings.Current?.PanicKey ?? "Escape";
        Title = "CCP - Emi show";
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        CanResize = false; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // WPF :73: the primary screen in DIPs, never the virtual desktop.
        var primary = Screens?.Primary ?? Screens?.All.FirstOrDefault();
        _screenPx = primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
        _scale = primary?.Scaling > 0 ? primary.Scaling : 1;
        // X11Overlay.cs:216: override-redirect goes on before Show() and before any geometry of ours.
        try { _redirected = X11Overlay.SetOverrideRedirect(this, _screenPx, passive: true); }
        catch (Exception ex) { Log.Debug(ex, "First show stage: override-redirect failed"); }
        Position = _screenPx.Position; Width = _screenPx.Width / _scale; Height = _screenPx.Height / _scale;
        Content = _stage;
        _logo.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .7, RelativeUnit.Relative),
            GradientStops = { new(Colors.White, 0), new(Colors.SlateGray, .24), new(Colors.White, .45), new(Colors.DimGray, .7), new(Colors.Silver, 1) }
        };
        try { using var art = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/ccp_banner.png")); _logo.Child = new Image { Source = new Bitmap(art), Stretch = Stretch.Uniform }; }
        catch (Exception ex) { Log.Debug("First show logo: {Error}", ex.Message); }
        _logo.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        _stage.Children.Add(_logo);
        Canvas.SetLeft(Credit, 16); Canvas.SetBottom(Credit, 12); _stage.Children.Add(Credit);   // WPF :90
        _loadingPanel.Children.Add(_loadingBar); _loadingPanel.Children.Add(_loadingStatus);
        _bubbleContent.Children.Add(_speech); _bubbleContent.Children.Add(_loadingPanel); _bubbleContent.Children.Add(_choices);
        // WPF :327-338: a line taller than the screen scrolls; normal lines show no scroll chrome (Auto).
        _bubble.Child = new ScrollViewer { Content = _bubbleContent, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _speechWindow.Content = _bubble;
        _x = Width - 205; _y = Height - 150;
        _clock = new FrameClock(_stage) { Interval = TimeSpan.FromMilliseconds(16) };
        _clock.Tick += (_, _) => Frame();
        Opened += (_, _) => _clock.Start();
        Closed += (_, _) => Cleanup();
        // P51: Tunnel, and only the stop keys, so a focused choice button cannot swallow them.
        AddHandler(KeyDownEvent, OnStopKey, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _speechWindow.AddHandler(KeyDownEvent, OnStopKey, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _stage.PointerPressed += (_, e) => { if (e.GetCurrentPoint(_stage).Properties.IsLeftButtonPressed && _playing && ShowTime < S.GatherAt) {
            _effects?.Pop(e.GetPosition(_stage).X, e.GetPosition(_stage).Y); UpdateBubbleInput();
        } };
    }
    /// <summary>Make the stage input-transparent and passive, then show it. False when the platform refuses.</summary>
    internal bool Launch()
    {
        bool ok = _redirected;   // both calls before the map, as every other overlay (BubbleOverlay.cs:81)
        try { ok &= X11Overlay.SetClickThrough(this, true); }
        catch (Exception ex) { ok = false; Log.Debug(ex, "First show stage: click-through failed"); }
        Show();
        // WPF ran its own keyboard hook so Esc stops the show even with the panic key off (:52, :92).
        if (OperatingSystem.IsWindows()) Win32PanicKey.KeyDown += OnGlobalKey;
        return ok;
    }
    private void OnGlobalKey(int vk, string? name)
    {
        if (vk == 0x1B || vk == ConditioningControlPanel.Input.VirtualKeys.Of(_panic))   // alias-aware: "Enter" is vk 0x0D
            Dispatcher.UIThread.Post(() => { if (!_closed) Close(); });
    }
    internal void OnStopKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || HotkeyNames.Matches(e.Key, _panic)) { e.Handled = true; Close(); }
    }
    private double ShowTime => double.IsNaN(_showStart) ? _showFrozen : Clock() - _showStart;
    private Point ToScreen(double x, double y) => new(Position.X + x * _scale, Position.Y + y * _scale);
    internal void BeginEntrance(EmiDeskWindow emi) { _emi = emi; emi.PresentAt(ToScreen(_x, _y), 0, 0, "idle", "^_^"); emi.RunPresentationEntrance(); }
    private void Say(string text, bool serious = false)
    {
        _loadingPanel.IsVisible = false; _serious = serious; _speech.Inlines?.Clear(); _speech.Text = text;
        if (_speechWindow.IsVisible) _speechWindow.Hide();
    }
    private void Ask(string text, bool serious = false) { _choices.Children.Clear(); Say(text, serious); }
    private void SetInlines(params Inline[] inlines) { _speech.Text = null; (_speech.Inlines ??= new InlineCollection()).AddRange(inlines); }
    private Run PanicRun() => new(S.PanicLabel(_panic)) { Foreground = Brushes.Red, FontWeight = FontWeight.Bold };
    private void AddButton(string label, Action action)
    {
        // A TextBlock, not a string: Avalonia's Button reads '_' as an access key.
        var button = new Button
        {
            Name = "Choice" + _choices.Children.Count, Content = new TextBlock { Text = label }, Margin = new Thickness(4), Padding = new Thickness(13, 8, 13, 8), FontSize = 14,
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(70, 35, 79)), BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => { _audio.Cue("click"); action(); };
        _choices.Children.Add(button);
    }
    private void Greeting() { Ask(Text("greeting")); AddButton(Text("yes"), Choose); AddButton(Text("later"), Close); }   // WPF :124
    private void Choose()
    {
        _centered = true; _moveAt = Clock();
        Ask(Text("pickQuestion") + "\n\n" + Text("pickHint"));
        foreach (var id in FirstShowPresets.All) AddButton(id == "censored" ? Text("censored") : id, () => _ = Load(id));
        AddButton(Text("later"), Close);
    }
    private static async Task<IReadOnlyList<FypAssetManifest.Entry>> DefaultFetch(string preset, int attempt, CancellationToken token)
    {
        // WPF :156-163: the provider's picture feed; a dry one retries on GIF stills.
        var source = new ScrolllerSource(preset == "cosplay", pageLimit: 8, preferTop: true);
        var sources = FirstShowPresets.Sources(preset);
        var channel = new FeedChannelState { Name = sources[attempt % sources.Length] };
        var kind = attempt == 0 ? FeedMediaKind.Image : FeedMediaKind.GifStill;
        return (await source.FetchPageAsync(channel, kind, token))?.Entries ?? new List<FypAssetManifest.Entry>();
    }
    /// <summary>WPF RemoteMediaCache.MaxSingleEntryBytes: one demo picture is never bigger than this.</summary>
    internal const long MaxPictureBytes = 24L * 1024 * 1024;
    /// <summary>The show's own temp dir, swept on every open, so a crash mid-show leaves nothing behind for long.</summary>
    internal static readonly string TempDir = Path.Combine(Path.GetTempPath(), "ccp-first-show");
    internal static void SweepTemp() { try { if (Directory.Exists(TempDir)) foreach (var f in Directory.GetFiles(TempDir)) Release(f); } catch (IOException) { } }
    private static async Task<string?> DownloadAsync(string url, CancellationToken token)
    {
        // WPF downloads through RemoteMediaCache: a Scrolller picture goes through the device cache (honest UA, charged).
        if (ScrolllerPolicy.IsScrolllerUrl(url)) return await CopyFromDeviceCacheAsync(url, token);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", ScrolllerSource.HonestUserAgent);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        var head = response.Content.Headers;
        if (!response.IsSuccessStatusCode || head.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true
            || head.ContentLength > MaxPictureBytes) return null;
        return await SaveCapped(await response.Content.ReadAsStreamAsync(token), token);
    }
    /// <summary>Test seam: the device cache read-or-fetch (refused while the share is spent or consent is missing).</summary>
    internal static Func<string, long, CancellationToken, Task<string?>> DeviceCacheFetch =
        (url, max, token) => ScrolllerDiskCache.GetOrFetchAsync(url, null, max, token);

    /// <summary>
    /// A Scrolller picture for the show (WPF RemoteMediaCache.MaterializeAsync): nothing while the licence switch is off;
    /// a cached copy is free, a fresh one only when the share allows it, downloaded by the device cache with the honest
    /// user-agent and charged. The show's copy carries the remote-copy prefix and is born at the download time, so the
    /// hourly sweep (Platform.ScrolllerHead) and the off switch reach it too.
    /// </summary>
    internal static async Task<string?> CopyFromDeviceCacheAsync(string url, CancellationToken token)
    {
        if (ScrolllerPolicyService.Blocks(url)) return null;
        var cached = await DeviceCacheFetch(url, MaxPictureBytes, token);
        if (cached == null) return null;
        var info = new FileInfo(cached);
        if (!info.Exists || info.Length > MaxPictureBytes) return null;
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, ScrolllerDiskCache.TempPrefix + Guid.NewGuid().ToString("N") + ScrolllerDiskCache.ExtensionOf(url));
        try
        {
            File.Copy(cached, path);
            ScrolllerDiskCache.StampBorn(path, ScrolllerDiskCache.DownloadedUtcOf(url) ?? info.CreationTimeUtc);
            return path;
        }
        catch (IOException) { Release(path); return null; }
        catch (UnauthorizedAccessException) { Release(path); return null; }
    }

    /// <summary>A show copy made from Scrolller (WPF ScrolllerCredit.IsScrolllerMedia on the temp copy).</summary>
    internal static bool IsScrolllerCopy(string path) => Path.GetFileName(path).StartsWith(ScrolllerDiskCache.TempPrefix, StringComparison.Ordinal);

    /// <summary>Copies at most <see cref="MaxPictureBytes"/> into <see cref="TempDir"/>; null (and no file) past the cap.</summary>
    internal static async Task<string?> SaveCapped(Stream body, CancellationToken token)
    {
        Directory.CreateDirectory(TempDir);
        var path = Path.Combine(TempDir, Guid.NewGuid().ToString("N"));
        bool keep = false;
        try
        {
            await using var file = File.Create(path);
            var buffer = new byte[81920]; long total = 0; int n;
            while ((n = await body.ReadAsync(buffer, token)) > 0)
            {
                if ((total += n) > MaxPictureBytes) return null;
                await file.WriteAsync(buffer.AsMemory(0, n), token);
            }
            keep = true;
            return path;
        }
        finally { await body.DisposeAsync(); if (!keep) Release(path); }
    }
    private static void Release(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    // WPF Load :145-191.
    internal async Task Load(string preset)
    {
        _load?.Cancel(); _load?.Dispose();
        var request = _load = new CancellationTokenSource();
        Ask(Text("loading")); AddButton(Text("bundled"), () => _ = Bundled()); AddButton(Text("later"), Close);
        var paths = new List<string>(); FirstShowEffects? candidate = null;
        try
        {
            int attempt = 0;
            paths = await FirstShowMediaLoader.LoadAsync(token => FetchFeed(preset, attempt++, token), Download, Release, request.Token,
                progress => Dispatcher.UIThread.Post(() => { if (!_closed && _load == request) ShowLoading(progress); }));
            if (paths.Count == 0) throw new IOException("No demo images available");
            ShowPreparing();
            candidate = new FirstShowEffects(_audio.Cue);
            await candidate.LoadAsync(paths);
            request.Token.ThrowIfCancellationRequested();
            if (_closed || _load != request) return;
            Adopt(candidate); candidate = null; _bundled = false;
            ReleaseFiles(); _files.AddRange(paths); paths.Clear();
            Confirm();
        }
        catch (Exception ex)
        {
            if (!request.IsCancellationRequested) Log.Warning("First show preset {Preset} unavailable: {Reason}", preset, ex.Message);
            if (!_closed && _load == request)
            {
                Ask(Text("error"));
                AddButton(Text("pickQuestion"), Choose); AddButton(Text("bundled"), () => _ = Bundled()); AddButton(Text("later"), Close);
            }
        }
        finally { candidate?.Dispose(); foreach (var path in paths) Release(path); }
    }
    private void ShowLoading(FirstShowMediaProgress progress)
    {
        _loadingPanel.IsVisible = true; _loadingBar.IsIndeterminate = progress.Finding;
        _loadingBar.Maximum = Math.Max(1, progress.Total); _loadingBar.Value = progress.Ready;
        _loadingStatus.Text = progress.Finding ? Text(progress.Attempt == 1 ? "loadFinding" : "loadRetry") :
            Text("loadDownload").Replace("{ready}", progress.Ready.ToString()).Replace("{total}", progress.Total.ToString());
    }
    private void ShowPreparing() { _loadingPanel.IsVisible = true; _loadingBar.IsIndeterminate = true; _loadingStatus.Text = Text("loadPreparing"); }
    // WPF Bundled :206-221.
    internal async Task Bundled()
    {
        _load?.Cancel(); _load?.Dispose(); _load = null;
        Ask(Text("loading")); ShowPreparing(); AddButton(Text("later"), Close);
        var effects = new FirstShowEffects(_audio.Cue);
        try
        {
            await effects.LoadAsync(BundledPictures());
            if (_closed) { effects.Dispose(); return; }
            Adopt(effects); _bundled = true; Confirm();
        }
        catch (Exception ex) { effects.Dispose(); Log.Debug(ex, "First show fallback unavailable"); if (!_closed) Choose(); }
    }
    private void Adopt(FirstShowEffects effects)
    {
        if (_effects != null) { _stage.Children.Remove(_effects); _effects.Dispose(); }
        effects.Width = Width; effects.Height = Height; _stage.Children.Insert(0, effects); _effects = effects;
    }
    // WPF Confirm :222-231: the warning, with the stop key in red.
    private void Confirm()
    {
        Ask(Text("warning"), true);
        SetInlines(new Run(Text("warning")), new LineBreak(), new LineBreak(), PanicRun(), new Run(" " + Text("panicHint")));
        AddButton(Text("ready"), Start); AddButton(Text("later"), Close);
    }
    internal void Start()
    {
        _choices.Children.Clear(); _serious = false; _playing = true; _ending = false; _beat = -1;
        _stage.Background = Brushes.Transparent; _showStart = Clock(); AddButton(Text("stop"), Close);
        // WPF :236 asks IsScrolllerMedia of each file; this head's show keeps its own temp names, and every
        // non-bundled picture came from the Scrolller feed (DefaultFetch).
        Credit.SetShown(!_bundled && _files.Count > 0);
    }
    // WPF Frame :239-318.
    internal void Frame()
    {
        if (_closed) return;
        // WPF FirstShowDesktopWindow.cs:99-104: Escape stops even without speech focus.
        if (ReadDesktopInput()?.Escape == true) { Close(); return; }
        var now = Clock(); var dt = Math.Min(.05, now - _last); _last = now;
        bool arriving = _emi?.PresentationArriving ?? true;
        if (!_greeted && now > S.GreetAfter && !arriving) { _greeted = true; Greeting(); }
        var time = ShowTime;
        if (_playing)
        {
            _effects?.Tick(time, dt);
            UpdateBubbleInput();
            while (_beat + 1 < S.Beats.Length && time >= S.Beats[_beat + 1])
            {
                _beat++;
                Say(Loc.Get(S.LineKey(_beat)).Replace("{panicKey}", S.PanicLabel(_panic))); _audio.Cue(S.Cues[_beat]);
                if (_beat == 0)
                {
                    var template = Text("beat0").Split("{panicKey}");
                    var runs = new List<Inline> { new Run(template[0]), PanicRun() };
                    if (template.Length > 1) runs.Add(new Run(template[1]));
                    SetInlines(runs.ToArray());
                }
            }
            if (time >= S.Length)
            {
                _playing = false; _ending = true; _showFrozen = time; _showStart = double.NaN; _stage.Background = null;
                Credit.SetShown(false); UpdateBubbleInput();   // WPF :264
                AskVerdict();
            }
        }
        var level = CoreSettings.Current?.MotionLevel ?? MotionLevel.Full;
        var m = level == MotionLevel.Off ? 0 : level == MotionLevel.Reduced ? .4 : 1;
        var move = _centered ? Math.Clamp((now - _moveAt) / .85, 0, 1) : 0; move = 1 - Math.Pow(1 - move, 3);
        if (m == 0 && _centered) move = 1;
        var previous = new Point(_x, _y);
        var body = BodySize();
        if (_guiding) GuideFrame(dt);
        else
        {
            _x = (Width - Math.Max(205, body.Width / 2 + 24)) * (1 - move) + Width * .5 * move;
            _y = (Height - Math.Max(150, body.Height / 2 + 24)) * (1 - move) + Height * .56 * move;
        }
        if (!_guiding && (_playing || _ending))
        {
            // EMI conducts from just below centre, then steps aside for the logo.
            _x = Width * .5; _y = Height * .56;
            if (time >= S.StepAsideAt)
            {
                var beside = FirstShowLayout.GuideBody(SafeBounds(), body, SpeechSize(), LogoBounds());
                double step = m == 0 ? 1 : Math.Clamp((time - S.StepAsideAt) / S.StepAsideSeconds, 0, 1);
                step = step * step * (3 - 2 * step);
                _x += (beside.Left + beside.Width / 2 - _x) * step;
                _y += (beside.Top + beside.Height / 2 - _y) * step;
            }
        }
        _moving = arriving || Math.Abs(_x - previous.X) + Math.Abs(_y - previous.Y) > .3 || (_centered && move < 1)
            || (!_guiding && (_playing || _ending) && time >= S.StepAsideAt && time < S.StepAsideAt + S.StepAsideSeconds);
        if (_moving) _settledAt = now;
        var bob = (Math.Sin(now * 2.5) * 3 + Math.Sin(now * 1.1) * 2) * m;
        var turn = Math.Sin(now * 1.5) * 1.6 * m;
        _emi?.PresentAt(ToScreen(_x, _y), bob, turn, S.Pose(_playing, _ending, time, m), S.Face(_serious, m, now));
        if (!_guiding && time >= S.RevealAt && (_playing || _ending))
        {
            var reveal = Math.Clamp((time - S.RevealAt) / S.RevealSeconds, 0, 1);
            _logo.Opacity = Math.Clamp(reveal * (m == 0 ? 1 : .95 + .05 * Math.Sin(now * 1.7)), 0, 1);
            _logo.RenderTransform = new ScaleTransform(.82 + .18 * reveal, .82 + .18 * reveal);
        }
        Place();
    }
    private void UpdateBubbleInput()
    {
        int n = _playing && _effects != null ? _effects.BubbleInputRects(_scale, ref _inputBuf) : 0;
        // Stayed empty: the region already is (SetClickThrough at open). A non-empty region is re-sent
        // every frame on purpose: the Win32 path re-reads the cursor on each call.
        if (n == 0 && InputRectCount == 0) return;
        InputRectCount = n; InputPushes++;
        X11Overlay.SetInputRects(this, _inputBuf, n);
    }
    private ShowSize BodySize() => _emi == null ? new ShowSize(0, 0) : new ShowSize(_emi.BodyWidth, _emi.BodyWidth * EmiDeskWindow.BodyAspect);
    private RectD SafeBounds()
    {
        var area = (_guiding && _main != null ? Screens?.ScreenFromWindow(_main) : Screens?.Primary)?.WorkingArea;
        if (area == null) return new RectD(16, 16, Math.Max(1, Width - 32), Math.Max(1, Height - 32));
        var a = area.Value;
        double ax = (a.X - Position.X) / _scale, ay = (a.Y - Position.Y) / _scale;
        return new RectD(ax + 16, ay + 16, Math.Max(1, a.Width / _scale - 32), Math.Max(1, a.Height / _scale - 32));
    }
    private ShowSize SpeechSize()
    {
        var bounds = SafeBounds();
        _bubble.Width = Math.Min(390, bounds.Width); _bubble.MaxHeight = bounds.Height;
        _bubble.Measure(new Size(_bubble.Width, _bubble.MaxHeight));
        return new ShowSize(_bubble.Width, Math.Max(1, _bubble.DesiredSize.Height));
    }
    private RectD LogoBounds()
    {
        _logo.Width = Math.Min(460, Math.Max(160, Width - 2 * (BodySize().Width + 48)));
        _logo.Measure(new Size(_logo.Width, double.PositiveInfinity));
        return new RectD(Width / 2 - _logo.Width / 2, Height * .43 - _logo.DesiredSize.Height / 2, _logo.Width, _logo.DesiredSize.Height);
    }
    // WPF Place :346-385: the speech card sits beside EMI once she has settled.
    private void Place()
    {
        var logo = LogoBounds();
        Canvas.SetLeft(_logo, logo.Left); Canvas.SetTop(_logo, logo.Top);
        if (!_greeted) return;
        if (_moving || Clock() - _settledAt < .16) { if (_speechWindow.IsVisible) _speechWindow.Hide(); return; }
        var size = BodySize(); var speech = SpeechSize();
        var body = new RectD(_x - size.Width / 2, _y - size.Height / 2, size.Width, size.Height);
        var rect = FirstShowLayout.Speech(SafeBounds(), speech, body, _guiding ? TargetRect : _logo.Opacity > 0 ? logo : FirstShowLayout.Empty, new Vec2(body.Right + 18, body.Top));
        var pixel = ToScreen(rect.Left, rect.Top);
        _speechWindow.Position = new PixelPoint((int)Math.Round(pixel.X), (int)Math.Round(pixel.Y));
        _speechWindow.Width = rect.Width; _speechWindow.Height = rect.Height;
        if (_speechWindow.IsVisible) return;
        try { if (_emi?.IsVisible == true) _speechWindow.Show(_emi); else _speechWindow.Show(); }
        catch (Exception ex) { Log.Debug(ex, "First show speech window failed to show"); }
    }
    private void ReleaseFiles() { foreach (var path in _files) Release(path); _files.Clear(); }
    private void Cleanup()
    {
        if (_closed) return;
        ClearGuideTarget();
        _closed = true;
        if (OperatingSystem.IsWindows()) Win32PanicKey.KeyDown -= OnGlobalKey;
        _clock.Stop();
        try { _speechWindow.Close(); } catch (Exception ex) { Log.Debug(ex, "First show speech window close failed"); }
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _effects?.Dispose(); _effects = null; _audio.Dispose(); ReleaseFiles();
    }
}
