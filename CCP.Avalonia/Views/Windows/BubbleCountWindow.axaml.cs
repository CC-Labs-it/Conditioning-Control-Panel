using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Video.Browser;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using LibVLCSharp.Shared;
using Difficulty = ConditioningControlPanel.Services.BubbleCountScheduler.Difficulty;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{

    /// <summary>
    /// Bubble Count Challenge - watch video, count bubbles, enter total.
    ///
    /// PORTED from ConditioningControlPanel/Windows/BubbleCountWindow.xaml.cs. The WPF original is
    /// 1862 lines and roughly six sevenths of it is Windows-only media plumbing that this head has
    /// no counterpart for. What SURVIVES the port intact is the game itself: difficulty -> target
    /// count, the spawn cadence, the shared single-timer bubble animation, the pop lifecycle, the
    /// shared multi-window state, ESC-to-skip, the strict-mode lock and the completion contract.
    ///
    /// <para><b>Win32 -> Avalonia/X11.</b> Every P/Invoke in the original is gone; none of
    /// user32/shcore is referenced here.</para>
    /// <list type="bullet">
    ///   <item><c>SetWindowLong(GWL_EXSTYLE, WS_EX_TOOLWINDOW)</c> to hide from Alt+Tab ->
    ///         <c>ShowInTaskbar="False"</c> in the XAML.</item>
    ///   <item><c>SetWindowPos(HWND_TOPMOST)</c> (ForceTopmost, and the same call in CountBubble)
    ///         -> <c>Topmost="True"</c>. Avalonia maps that to <c>_NET_WM_STATE_ABOVE</c>, which is
    ///         the correct X11 mechanism, so nothing extra is needed (see X11Overlay's own note).
    ///         The bubble-above-game ordering, which HWND_TOPMOST alone never guaranteed either,
    ///         is pinned explicitly with <see cref="X11Overlay.RestackAbove"/>.</item>
    ///   <item><c>IsHitTestVisible=false</c> on a bubble window only stops Avalonia's own hit
    ///         testing, not the compositor's, so a bubble would still eat clicks meant for the
    ///         video. <see cref="X11Overlay.SetClickThrough"/> is the real equivalent of the
    ///         original's implicit click-through and is applied after Show().</item>
    ///   <item><c>MonitorFromPoint</c> + <c>GetDpiForMonitor</c> (GetDpiForScreen) -> Avalonia's
    ///         <c>Screen.Scaling</c>. The division by DPI that the original does on Left/Top also
    ///         goes: WPF's Left/Top are DIPs, Avalonia's <c>Window.Position</c> is a PixelPoint in
    ///         PHYSICAL pixels, which is what <c>Screen.WorkingArea</c> already is.</item>
    ///   <item><c>NoClickRaiseHook</c> (an HwndSource hook returning MA_NOACTIVATE for
    ///         WM_MOUSEACTIVATE) uses Win32Window on Windows; on X11 live bubbles are restacked after pointer presses and activation.</item>
    ///   <item><c>DisableProcessWindowsGhosting</c>-class calls: none in this file to drop.</item>
    /// </list>
    ///
    /// <para><b>Video.</b> One decoder on an isolated LibVLC through
    /// <see cref="VlcFrameSink"/>, its frames shown in an <c>Image</c> in every game window (WPF ran
    /// one muted player per secondary). Accepted files use guarded browser playback.</para>
    ///
    /// <para><b>Wired:</b> the pop sound (<c>CoreAudio.PlayOneShot</c> at the WPF
    /// <c>(master * bubbles) ^ 1.5</c> volume), the monitor set (<c>DualMonitorEnabled</c> from
    /// <c>CoreSettings</c>, screens from <c>ScreenList.Enumerate</c>) and the result window, so a
    /// finished game asks for the count and resolves on the answer instead of being written off.</para>
    ///
    /// <para><b>Stubbed:</b> the shared LibVLC retire/rebuild and VideoDiag.
    /// The game shares the mandatory-video metadata cache.</para>
    ///
    /// <para>Opened validates the path synchronously so an invalid clip completes once inside Show().
    /// Loaded starts playback after layout, as WPF does. Starting in Opened lets a short clip finish
    /// while a cold window is still laying out, before the user sees the game.</para>
    /// </summary>
    public partial class BubbleCountWindow : Window
    {
        private bool _closed, _useBrowser, _browserFailed;
        private readonly BubbleCountWatchCredit _watch = new();
        internal Func<double> WatchNow = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        internal Action<double> WatchCredit = seconds => Dispatcher.UIThread.Post(() => CoreProgression.TrackVideoWatched(seconds));

        internal void MarkWatchPlaying() { lock (_watch) _watch.MarkPlaying(WatchNow()); }
        internal void NoteWatchPosition(double seconds) { lock (_watch) _watch.NotePosition(seconds); }
        internal void CreditWatchTime()
        {
            try
            {
                double seconds;
                lock (_watch) seconds = _watch.Take(WatchNow(), _videoDurationSeconds);
                if (seconds >= 1) WatchCredit(seconds);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Bubble Count watch credit failed"); }
        }
        private BrowserVideoSurface? _browser;
        private static BubbleCountWindow? _browserOwner;
        internal BrowserVideoSurface? Browser => _browser;
        private readonly string _videoPath;
        private readonly Difficulty _difficulty;
        private readonly bool _strictMode;
        private readonly Action<bool> _onComplete;
        private readonly Action _onSkipped;
        private readonly Screen? _screen;
        private readonly bool _isPrimary;

        private readonly Random _random = new();
        private readonly List<CountBubble> _activeBubbles = new();
        private DispatcherTimer? _bubbleSpawnTimer;
        private DispatcherTimer? _safetyTimer;
        // Single shared animation timer for ALL count bubbles, replacing the previous
        // two-DispatcherTimers-per-bubble model (up to ~24 concurrent timers).
        private DispatcherTimer? _bubbleAnimTimer;
        private const double BubbleAnimTickMs = 30;

        private int _bubbleCount = 0;
        private int _targetBubbleCount = 0;
        private double _videoDurationSeconds = 30;
        private bool _videoEnded = false;
        private bool _gameCompleted = false;

        /// <summary>The bubble artwork - the mod's override or this head's own bubble.png - or
        /// null to draw the gradient ellipse fallback. See <see cref="LoadBubbleImage"/>.</summary>
        private IImage? _bubbleImage;

        // Every window shows the one shared decoder's frames.
        private readonly Image _videoImage = new() { Stretch = Stretch.Uniform };
        private static MediaPlayer? _player;
        private static LibVLC? _gameVlc;
        private static Media? _media;
        private static VlcFrameSink? _sink;

        /// <summary>The global panic listener can stop the game right now (as MandatoryVideoOverlay). Tests swap it.</summary>
        internal static Func<bool> PanicListenerLive = () => PanicListeners.Live;
        internal static VlcFrameSink? Sink => _sink;
        internal static MediaPlayer? Player => _player;

        // Multi-monitor support - static shared state
        private static readonly object _cleanupLock = new();
        private static bool _isCleaningUp = false;
        private static int _completionGeneration;
        private static readonly List<BubbleCountWindow> _allWindows = new();
        private static int _sharedBubbleCount = 0;
        private static int _sharedTargetCount = 0;

        /// <summary>Fallback duration when the metadata cache has never seen this video.</summary>
        private const double FallbackDurationSeconds = 30;

        private readonly Grid _videoContainer;
        private readonly TextBlock _txtDifficulty, _txtStrict, _txtEscHint;

        /// <summary>Duration of the last played video in seconds (shared for XP scaling)</summary>
        internal static double LastVideoDurationSeconds { get; private set; } = 30;

        /// <summary>
        /// Render/design constructor: a game window with no game running, so --render-all can draw
        /// it. Everything it does beyond the real ctor exists because starting a game headlessly
        /// would be worse than useless: <see cref="OnOpened"/> aborts on a nonexistent video file
        /// and closes the window mid-render, and the safety/spawn timers it starts would outlive
        /// this view and tick through every LATER view in the same --render-all process.
        /// </summary>
        internal BubbleCountWindow() : this("sample.mp4", Difficulty.Medium, false, _ => { })
        {
            Opened -= OnOpened;
            // Not "the game was won" - "no game is running on this window", which is what every
            // read of this flag actually asks. Keeps the ESC handler and the focus-reclaim loop
            // inert during the render.
            _gameCompleted = true;
            Width = 1280;
            Height = 720;
        }

        public BubbleCountWindow(string videoPath, Difficulty difficulty,
            bool strictMode, Action<bool> onComplete,
            Screen? screen = null, bool isPrimary = true, Action? onSkipped = null)
        {
            AvaloniaXamlLoader.Load(this);

            _videoContainer = this.FindControl<Grid>("VideoContainer")!;
            _txtDifficulty = this.FindControl<TextBlock>("TxtDifficulty")!;
            _txtStrict = this.FindControl<TextBlock>("TxtStrict")!;
            _txtEscHint = this.FindControl<TextBlock>("TxtEscHint")!;

            _videoPath = videoPath;
            _difficulty = difficulty;
            _strictMode = strictMode;
            _onComplete = onComplete;
            _onSkipped = onSkipped ?? (() => onComplete(false));
            // Screens can be empty under a headless platform, so this stays nullable and every
            // read of it is guarded. WPF's Screen.PrimaryScreen! could not be null.
            _screen = screen ?? Screens?.Primary ?? Screens?.All.FirstOrDefault();
            _isPrimary = isPrimary;
            _videoContainer.Children.Add(_videoImage);

            // Set difficulty display. A local value, not a {loc:Str} binding - see the XAML.
            _txtDifficulty.Text = $" ({difficulty})";

            // Handle strict mode
            if (_strictMode)
            {
                _txtStrict.IsVisible = true;
                _txtEscHint.IsVisible = false;
            }

            // Initial small position on target screen (will maximize after show).
            // No DPI division here, unlike WPF: Position is in physical pixels and so is Bounds.
            if (_screen != null)
            {
                Position = new PixelPoint(_screen.Bounds.X + 100, _screen.Bounds.Y + 100);
            }
            Width = 400;
            Height = 300;

            // Load bubble image
            LoadBubbleImage();

            // Key handler
            KeyDown += OnKeyDown;

            // WPF BubbleCountWindow.xaml.cs:150-161, 1832-1840 denies click raises.
            // X11 can raise before Avalonia receives input, so restore the bubbles afterwards.
            AddHandler(PointerPressedEvent, (_, _) => QueueBubbleRestack(),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Activated += (_, _) => QueueBubbleRestack();

            // Reclaim focus when stolen by other windows (only primary needs focus)
            if (_isPrimary)
            {
                Deactivated += (s, e) =>
                {
                    if (!_gameCompleted && !_videoEnded)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!_gameCompleted && !_videoEnded)
                            {
                                // SetForegroundWindow's mapping. Focus() is implied by Activate()
                                // on a window with no focused child.
                                Activate();
                                Focus();
                            }
                        }, DispatcherPriority.Input);
                    }
                };
            }

            // Register window
            _allWindows.Add(this); System.Threading.Volatile.Write(ref _videoWindowCount, _allWindows.Count);

            // Start when opened
            Opened += OnOpened;
        }

        /// <summary>
        /// Show bubble count game on all monitors.
        /// </summary>
        /// <param name="onSkipped">Optional "the game never happened" resolution. A skip is NOT a
        /// loss: routing it through <paramref name="onComplete"/> with false would read as a failed
        /// count and, in strict mode, start the WRONG! WATCH AGAIN retry loop for a game the user
        /// never saw. When null, a skip falls back to the completion callback (old behaviour).</param>
        public static void ShowOnAllMonitors(string videoPath, Difficulty difficulty,
            bool strictMode, Action<bool> onComplete, Action? onSkipped = null)
        {
            // The completion callback must reach the caller EXACTLY once. Every window shares this
            // delegate and CloseAllWindows invokes it, which can happen INSIDE the Show() below
            // (Opened runs synchronously there, and a start failure closes the game from it) - so
            // the catch at the bottom would otherwise deliver a second, contradictory completion.
            int completionDelivered = 0;
            Action<bool> complete = ok =>
            {
                if (System.Threading.Interlocked.Exchange(ref completionDelivered, 1) != 0) return;
                BubbleCountWedgeWatchdog.Game.Disarm();
                onComplete?.Invoke(ok);
            };

            Action skip = () =>
            {
                if (System.Threading.Interlocked.Exchange(ref completionDelivered, 1) != 0) return;
                BubbleCountWedgeWatchdog.Game.Disarm();
                if (onSkipped != null) onSkipped(); else onComplete(false);
            };

            // A deferred completion from a previous game must not resolve this one.
            _completionGeneration++;
            // Reset shared state
            lock (_cleanupLock)
            {
                _isCleaningUp = false;
            }
            var orphanWindows = _allWindows.ToList();
            StopPlayer();
            _allWindows.Clear(); System.Threading.Volatile.Write(ref _videoWindowCount, _allWindows.Count);
            _sharedBubbleCount = 0;
            _sharedTargetCount = 0;

            // Only reachable when a previous game threw partway through its show/teardown;
            // normally the list is already empty.
            foreach (var window in orphanWindows)
            {
                try
                {
                    window._safetyTimer?.Stop();
                    window._bubbleSpawnTimer?.Stop();
                    window.Close();
                }
                catch { }
            }

            // One screen or all of them, per the user's setting - the same question
            // BubbleCountResultWindow.ShowOnAllMonitors asks, answered from the same place.
            //
            // Avalonia has no screen list without a TopLevel, so the primary is built first and its
            // Screens is what the set is drawn from; WPF read App.GetAllScreensCached() up front.
            // An empty enumeration (headless, or no platform impl yet) is the single-screen path,
            // NOT WPF's onComplete(false): here empty means "no topology reported", not "no display".
            BubbleCountWedgeWatchdog.Game.Arm();
            try
            {
                var primaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, complete, null, true, skip);
                primaryWindow._useBrowser = BrowserVideoSurface.TryAcquire(primaryWindow, videoPath);
                if (primaryWindow._useBrowser) { _browserOwner = primaryWindow; BubbleCountWedgeWatchdog.Game.Disarm(); }
                primaryWindow.Show();
                if (primaryWindow._closed) return;
                primaryWindow.WindowState = WindowState.Maximized;

                if (CoreSettings.Current.DualMonitorEnabled)
                {
                    var all = Features.ScreenList.Enumerate(primaryWindow);
                    // Drawn from the SAME list the loop filters, never from primaryWindow._screen:
                    // that came from Screens?.Primary in the ctor and Screen does not promise
                    // reference equality across reads, so a mismatch would open a second fullscreen
                    // game on the primary display.
                    var primary = all.FirstOrDefault(s => s.IsPrimary) ?? all.FirstOrDefault();
                    foreach (var screen in all.Where(s => s != primary))
                    {
                        var secondary = new BubbleCountWindow(videoPath, difficulty, strictMode, complete, screen, false)
                        { _useBrowser = primaryWindow._useBrowser };
                        secondary.Show();
                        secondary.WindowState = WindowState.Maximized;
                    }
                }

                // Activate the primary LAST so it owns the keyboard, exactly as WPF does.
                // ForceTopmost's SetWindowPos(HWND_TOPMOST) is gone: Topmost="True" in the XAML is
                // the same thing, applied by the platform rather than after the fact.
                primaryWindow.Activate();
            }
            catch
            {
                ForceCloseAll();
                complete(false);
            }
        }

        /// <summary>
        /// Force close all bubble count windows (used by panic button)
        /// </summary>
        public static void ForceCloseAll()
        {
            BubbleCountWedgeWatchdog.Game.Disarm();
            _completionGeneration++;
            lock (_cleanupLock)
            {
                if (_isCleaningUp) return;
                _isCleaningUp = true;
            }

            try
            {
                var primary = _allWindows.FirstOrDefault(w => w._isPrimary);
                primary?.CreditWatchTime();
                StopPlayer();
                var windowsCopy = _allWindows.ToList();
                _allWindows.Clear(); System.Threading.Volatile.Write(ref _videoWindowCount, _allWindows.Count);
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        window._safetyTimer?.Stop();
                        window._bubbleSpawnTimer?.Stop();
                        window.Close();
                    }
                    catch { }
                }
                StopPlayer();
            }
            finally
            {
                lock (_cleanupLock)
                {
                    _isCleaningUp = false;
                }
            }
        }

        /// <summary>
        /// Check if any bubble count window is currently open
        /// </summary>
        private static int _videoWindowCount;
        internal static bool VideoSessionActive => System.Threading.Volatile.Read(ref _videoWindowCount) > 0;
        public static bool IsAnyOpen() => _allWindows.Count > 0;
        internal static IReadOnlyList<BubbleCountWindow> OpenWindows => _allWindows;
        internal Image VideoImage => _videoImage;

        private void OnOpened(object? sender, EventArgs e)
        {
            if (!_useBrowser) BubbleCountWedgeWatchdog.Game.Register(this);

            // WPF VideoService.cs:8709: clicking the game must not raise it over count bubbles.
            // X11 still needs its own deny-focus-on-click implementation.
            Platform.Win32Window.DenyMouseActivate(this);

            try
            {
                // WPF: a missing clip aborts the game (a loss, as there).
                if (string.IsNullOrWhiteSpace(_videoPath) || !File.Exists(_videoPath))
                {
                    if (_isPrimary) CloseAllWindows(false);
                    return;
                }
                if (_useBrowser)
                {
                    if (StartBrowserPlayback()) return;
                    if (!_isPrimary) return;
                    BrowserVideoSurface.Release(this);
                    _browserOwner = null;
                    _useBrowser = false;
                    BubbleCountWedgeWatchdog.Game.Arm();
                    BubbleCountWedgeWatchdog.Game.Register(this);
                }
                // WPF :308-319 skips a poisoned native start, including browser fallback.
                if (_isPrimary && NativeBlocked)
                {
                    ForceCloseAll();
                    _onSkipped();
                    return;
                }
                if (!_isPrimary)
                {
                    _targetBubbleCount = _sharedTargetCount;
                    _videoImage.Source = _sink?.Bitmap;
                    return;
                }
                // WPF OnLoaded (BubbleCountWindow.xaml.cs:508): do not spend the clip while
                // Show is still laying out a cold window. Missing-file failure above stays synchronous.
                void StartWhenLoaded(object? s, global::Avalonia.Interactivity.RoutedEventArgs args)
                {
                    Loaded -= StartWhenLoaded;
                    try { if (!_gameCompleted && !_isCleaningUp && IsVisible && !StartPlayback()) CloseAllWindows(false); }
                    catch { CloseAllWindows(false); }
                }
                Loaded += StartWhenLoaded;
            }
            catch
            {
                if (_isPrimary) CloseAllWindows(false);
            }
        }

        /// <summary>
        /// Replace the estimated duration with the one the player actually reports, and re-scale
        /// the game clock around it. Only the primary owns the clock.
        /// </summary>
        private void AdoptRealDuration(double seconds)
        {
            if (!_isPrimary || _closed || _gameCompleted || _videoEnded || _isCleaningUp) return;
            if (!double.IsFinite(seconds) || seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds - 120 || Math.Abs(seconds - _videoDurationSeconds) < 1.0) return;   // WPF BubbleCountWindow.xaml.cs:727 ignores sub-second changes

            _videoDurationSeconds = seconds;
            LastVideoDurationSeconds = seconds;
            CalculateTargetBubbles();
            _sharedTargetCount = _targetBubbleCount;
            // WPF BubbleCountWindow.xaml.cs:736-748: keep every window's target and the
            // active spawn cadence in step with the corrected clock.
            foreach (var window in _allWindows) window._targetBubbleCount = _targetBubbleCount;
            // Same cadence as StartBubbleSpawning (a spawn timer, not a frame loop).
            if (_bubbleSpawnTimer != null)
                _bubbleSpawnTimer.Interval = TimeSpan.FromMilliseconds(
                    (_videoDurationSeconds * 1000) / Math.Max(1, _targetBubbleCount) * 0.7);
            StartSafetyTimer(_videoDurationSeconds);
        }

        // WPF BubbleCountWindow.xaml.cs:705-718: shared cache, asynchronous warm on miss,
        // and a 30 s fallback. No metadata parse blocks the UI thread.
        internal static double ResolveVideoDurationSeconds(string path)
        {
            try
            {
                if (Overlays.MandatoryVideoOverlay.LengthOf(path) is double seconds && seconds > 0) return seconds;
            }
            catch { }
            return FallbackDurationSeconds;
        }

        // The game clock: the cached duration or the 30 s fallback, then the safety timer and bubbles.
        // WPF BubbleCountWindow.xaml.cs:293-295, 833-898, 935-1010, 1053-1080.
        // Keep strict failures on the existing retry flow; only media errors poison the file.
        private void InitializePlaybackClock()
        {
            _videoDurationSeconds = ResolveVideoDurationSeconds(_videoPath);
            LastVideoDurationSeconds = _videoDurationSeconds;
            CalculateTargetBubbles();
            _sharedTargetCount = _targetBubbleCount;
            StartSafetyTimer(_videoDurationSeconds);
            StartBubbleSpawning();
        }

        private bool StartBrowserPlayback()
        {
            var url = BrowserVideoSurface.Map(_videoPath);
            if (url == null) return false;
            if ((_browser = BrowserVideoSurface.Create()) == null) { if (_isPrimary) BrowserVideoSurface.StoodDown = true; return false; }
            _videoImage.IsVisible = false;
            _videoContainer.Children.Add(_browser.View);
            _browser.Message += OnBrowserMessage;
            _browser.Failed += OnBrowserFailure;
            if (_isPrimary) InitializePlaybackClock(); else _targetBubbleCount = _sharedTargetCount;
            _browser.Start(url, _isPrimary, CoreSettings.Current.MasterVolume);
            return true;
        }

        // WPF BubbleCountWindow.xaml.cs:1053-1065: latch and snapshot before posting.
        private void OnBrowserFailure(bool mediaFailure)
        {
            if (!_isPrimary || _closed || _gameCompleted || _videoEnded || _browserFailed) return;
            _browserFailed = true;
            if (!mediaFailure) BrowserVideoSurface.StoodDown = true;
            var played = _browser?.Playing == true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || _gameCompleted || _videoEnded) return;
                if (played) OnVideoEnded(); else CloseAllWindows(false);
            });
        }

        private void OnBrowserMessage(JsonElement message)
        {
            if (_closed || _gameCompleted || _videoEnded || _browserFailed) return;
            if (message.GetProperty("type").GetString() == "click") { QueueBubbleRestack(); return; }
            if (!_isPrimary) return;
            switch (message.GetProperty("type").GetString())
            {
                case "playing":
                    MarkWatchPlaying();
                    break;
                case "error":
                    if (message.TryGetProperty("code", out var error) && error.TryGetInt32(out var code)
                        && (code is >= 1 and <= 4 or 100 or 101))
                        BrowserUnsafeVideoCache.Add(_videoPath, "bubble-count media error");
                    break;
                case "meta":
                    if (message.TryGetProperty("durationMs", out var d) && d.TryGetDouble(out var ms)) AdoptRealDuration(ms / 1000);
                    break;
                case "ended":
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_closed || _gameCompleted || _videoEnded) return;
                        if (_browser?.Playing == true) OnVideoEnded(); else CloseAllWindows(false);
                    });
                    break;
                case "key":
                    if (message.TryGetProperty("key", out var key))
                    {
                        var name = key.GetString();
                        if (HotkeyNames.TryParse(name == " " ? "Space" : name, out var parsed))
                        {
                            var modifiers = message.TryGetProperty("alt", out var alt) && alt.ValueKind == JsonValueKind.True ? KeyModifiers.Alt : KeyModifiers.None;
                            Dispatcher.UIThread.Post(() => { if (!_closed) OnKeyDown(this, new KeyEventArgs { Key = parsed, KeyModifiers = modifiers }); });
                        }
                    }
                    break;
            }
        }

        private bool StartPlayback()
        {
            if (LibVlcAudio.Shared == null) return false;
            InitializePlaybackClock();
            var vlc = CreateGameEngine();
            _gameVlc = vlc;

            var player = new MediaPlayer(vlc) { EnableHardwareDecoding = true };
            player.Volume = Math.Clamp(CoreSettings.Current.MasterVolume, 0, 100);
            lock (_nativeStopGate) _player = player;
            var media = new Media(vlc, _videoPath, FromType.FromPath);
            _media = media;
            _sink = new VlcFrameSink(player, () => media,
                bmp => { if (ReferenceEquals(player, _player)) foreach (var w in _allWindows) w._videoImage.Source = bmp; },
                () => { if (ReferenceEquals(player, _player)) foreach (var w in _allWindows) w._videoImage.InvalidateVisual(); });
            // LibVLC raises these on its own thread; the game is torn down only on the UI thread.
            player.Playing += (_, _) => MarkWatchPlaying();
            player.TimeChanged += (_, a) => NoteWatchPosition(a.Time / 1000.0);
            player.LengthChanged += (_, a) => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(player, _player)) AdoptRealDuration(a.Length / 1000.0); });
            player.EndReached += (_, _) => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(player, _player)) OnVideoEnded(); });
            player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(player, _player)) OnVideoEnded(); });
            var onPlaying = NativePlayingHandler(player);
            player.Playing += (_, _) => onPlaying();
            // WPF 7.2.2 a0b6d5779 (#1549): the primary video names itself in the recent-sounds ring;
            // running = engine or session (WPF EngineOffAudioLog.cs:29), a paused session is not engine-off.
            if (player.Play(media)) Services.Audio.RecentSounds.Shared.Note("bubblecount-video", _videoPath, CoreEngine.IsRunning || CoreSession.IsSessionRunning, false, DateTime.Now);
            return true;
        }

        // Route on the UI thread, which also owns teardown and the player reference.
        internal Action<MediaPlayer, int> RouteAudio = (player, volume) =>
        {
            LibVlcAudio.ApplyPreferredDevice(player);
            player.Volume = volume;
        };

        internal Action NativePlayingHandler(MediaPlayer player)
        {
            var routed = 0;
            return () =>
            {
                if (System.Threading.Interlocked.Exchange(ref routed, 1) != 0) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_closed || _videoEnded || _gameCompleted || _isCleaningUp || !ReferenceEquals(player, _player)) return;
                    RouteAudio(player, Math.Clamp(CoreSettings.Current.MasterVolume, 0, 100));
                });
            };
        }

        internal static LibVLC CreateGameEngine()
        {
            var engine = LibVlcAudio.CreateIsolatedVideo();
            if (ReferenceEquals(engine, LibVlcAudio.Shared))
                throw new InvalidOperationException("Bubble Count requires an isolated video engine");
            return engine;
        }

        // WPF BubbleCountWindow.xaml.cs:1435-1469, 1509-1548: never hold the UI
        // indefinitely on native Stop. Keep the complete graph alive if it does not settle.
        private static readonly List<object> _quarantinedNative = new();
        internal static volatile bool NativeUnavailable;
        internal static Func<long> NativeClock = () => Environment.TickCount64;
        private sealed class NativeHold { public long From; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, NativeHold> _nativeHolds = new();
        // WPF VideoService.cs:170,1320-1344 and VideoCooldownRules.cs:16: four seconds
        // bounded Stop, four seconds straggler grace, then a 750ms settle and 60s hold.
        internal static bool NativeBlocked
        {
            get
            {
                lock (_quarantinedNative)
                {
                    if (!NativeUnavailable) return false;
                    if (_quarantinedNative.Count == 0) return true;
                    var now = NativeClock();
                    return _quarantinedNative.Any(r => !_nativeHolds.TryGetValue(r, out var hold) ||
                        (now >= hold.From && now - hold.From < 60000));
                }
            }
        }

        internal static bool StopNative(Action stop, Action dispose, Action freeSink, object resources, int budgetMs,
            Action<Action>? post = null, Action? settled = null)
        {
            post ??= action => Dispatcher.UIThread.Post(action);
            var started = NativeClock();
            var pending = Task.Run(stop);
            var needsDispose = true;
            var sinkFailed = false;
            try
            {
                if (pending.Wait(budgetMs))
                {
                    pending = Task.Run(dispose);
                    needsDispose = false;
                    if (pending.Wait(budgetMs))
                    {
                        try { freeSink(); settled?.Invoke(); return true; }
                        catch { sinkFailed = true; }
                    }
                }
            }
            catch { /* Keep failed native resources rooted. */ }

            // Root the graph immediately. A fresh isolated engine may start during grace and
            // after cooldown, while the original graph stays rooted until cleanup succeeds.
            lock (_quarantinedNative)
            {
                _quarantinedNative.Add(resources);
                _nativeHolds.Add(resources, new NativeHold { From = sinkFailed || pending.IsFaulted ? NativeClock() : started + 8750 });
                NativeUnavailable = true;
            }
            if (!sinkFailed) _ = FinishNativeAsync();
            return false;

            async Task FinishNativeAsync()
            {
                try
                {
                    await pending.ConfigureAwait(false);
                    if (needsDispose) await Task.Run(dispose).ConfigureAwait(false);
                    post(() =>
                    {
                        try
                        {
                            freeSink(); // Bitmap and its callbacks belong to the UI thread.
                            lock (_quarantinedNative)
                            {
                                var index = _quarantinedNative.FindIndex(item => ReferenceEquals(item, resources));
                                if (index >= 0) _quarantinedNative.RemoveAt(index);
                                _nativeHolds.Remove(resources);
                                NativeUnavailable = _quarantinedNative.Count != 0;
                            }
                            settled?.Invoke();
                        }
                        catch (Exception ex) { lock (_quarantinedNative) _nativeHolds.GetValue(resources, _ => new NativeHold()).From = NativeClock(); Serilog.Log.Warning(ex, "Bubble count sink cleanup failed"); }
                    });
                }
                catch (Exception ex) { lock (_quarantinedNative) _nativeHolds.GetValue(resources, _ => new NativeHold()).From = NativeClock(); Serilog.Log.Warning(ex, "Bubble count native teardown failed; player quarantined"); }
            }
        }

        // Share an in-flight stop with teardown. If startup resumes after a stop completed,
        // teardown needs a fresh stop before disposing the player.
        private static readonly object _nativeStopGate = new();
        private static object? _nativeStopOwner;
        private static Task? _nativeStopTask;

        internal static Task QueueNativeStop(object owner, Action stop)
        {
            lock (_nativeStopGate)
            {
                if (!ReferenceEquals(_nativeStopOwner, owner) || _nativeStopTask == null || _nativeStopTask.IsCompleted)
                {
                    _nativeStopOwner = owner;
                    _nativeStopTask = Task.Run(stop);
                }
                return _nativeStopTask!;
            }
        }

        internal static void StopForWedge()
        {
            lock (_nativeStopGate)
            {
                var player = _player;
                if (player != null) _ = QueueNativeStop(player, player.Stop);
            }
        }

        private static Task StopPlayer()
        {
            foreach (var window in _allWindows)
            {
                window._browser?.Dispose();
                window._browser = null;
            }
            if (_browserOwner != null) BrowserVideoSurface.Release(_browserOwner);
            _browserOwner = null;
            MediaPlayer? player;
            Task stopped;
            lock (_nativeStopGate)
            {
                player = _player;
                _player = null;
                stopped = player == null ? Task.CompletedTask : QueueNativeStop(player, player.Stop);
                _nativeStopOwner = null;
                _nativeStopTask = null;
            }
            var media = _media;
            var sink = _sink;
            var vlc = _gameVlc;
            _gameVlc = null;
            _media = null;
            _sink = null;
            foreach (var w in _allWindows) w._videoImage.Source = null;
            if (player == null && media == null && sink == null && vlc == null) return Task.CompletedTask;
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            StopNative(() => stopped.GetAwaiter().GetResult(), () =>
            {
                player?.Dispose();
                media?.Dispose();
                vlc?.Dispose();
            }, () => sink?.Free(), (player, media, sink, vlc), 500, settled: () => settled.TrySetResult());
            return settled.Task;
        }

        // WPF BubbleCountWindow.xaml.cs:88-95, 1435-1478 waits up to four seconds
        // before delivering a result. Do not hold Avalonia's dispatcher during that wait.
        internal static async Task CompleteAfterStop(Task settled, TimeSpan remaining, Action complete,
            Func<TimeSpan, Task>? delay = null, Action<Action>? post = null)
        {
            await Task.WhenAny(settled, (delay ?? Task.Delay)(remaining)).ConfigureAwait(false);
            (post ?? (action => Dispatcher.UIThread.Post(action)))(complete);
        }

        private void CalculateTargetBubbles() =>
            _targetBubbleCount = BubbleCountScheduler.TargetBubbles(_difficulty, _videoDurationSeconds, _random.NextDouble());

        /// <summary>
        /// The bubble sprite: the mod's <c>bubble.png</c> if it ships one, else this head's copy
        /// (<c>Assets/bubble.png</c>, linked as <c>avares://CCP.Avalonia/Resources/bubble.png</c>).
        /// Null keeps CountBubble's gradient ellipse, which is the WPF original's own fallback for
        /// an image that will not load, so a miss degrades rather than blanks.
        /// </summary>
        private void LoadBubbleImage()
        {
            _bubbleImage = Helpers.ModArt.TryLoad("bubble.png");
        }

        private void StartSafetyTimer(double videoDurationSeconds)
        {
            _safetyTimer?.Stop();

            var timeoutSeconds = videoDurationSeconds + 5;

            _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            _safetyTimer.Tick += (s, e) =>
            {
                _safetyTimer?.Stop();
                if (!_closed && !_gameCompleted && !_videoEnded && !_isCleaningUp)
                {
                    if (_useBrowser && _browser?.Playing != true) CloseAllWindows(false); else OnVideoEnded();
                }
            };
            _safetyTimer.Start();
        }

        private void StartBubbleSpawning()
        {
            if (!_isPrimary) return;

            var intervalMs = (_videoDurationSeconds * 1000) / Math.Max(1, _targetBubbleCount);

            _bubbleSpawnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs * 0.7)
            };

            _bubbleSpawnTimer.Tick += (s, e) =>
            {
                if (_sharedBubbleCount < _targetBubbleCount && !_videoEnded && !_isCleaningUp)
                {
                    if (_random.NextDouble() < 0.7 || _sharedBubbleCount < _targetBubbleCount / 2)
                    {
                        SpawnBubbleOnAllWindows();
                    }
                }
            };

            // Delay bubble spawning until layout is complete. WPF used
            // Task.Delay(1500).ContinueWith(dispatcher.Invoke); RunOnce is the same thing without
            // the thread hop, and the null check is what makes a close during the delay a no-op.
            DispatcherTimer.RunOnce(() =>
            {
                if (_videoEnded || _isCleaningUp || _bubbleSpawnTimer == null) return;
                _bubbleSpawnTimer.Start();
                SpawnBubbleOnAllWindows();
            }, TimeSpan.FromMilliseconds(1500));
        }

        private void SpawnBubbleOnAllWindows()
        {
            if (_sharedBubbleCount >= _targetBubbleCount) return;
            _sharedBubbleCount++;
            _bubbleCount = _sharedBubbleCount;

            // Random position (relative 0-1)
            var relX = _random.NextDouble() * 0.7 + 0.15;
            var relY = _random.NextDouble() * 0.5 + 0.25;
            var size = _random.Next(120, 225);

            // Spawn on ONE random window - posted at Background priority so it never competes with
            // the video surface's own drawing, exactly as in WPF.
            var windows = _allWindows.ToList();
            if (windows.Count > 0)
            {
                var randomWindow = windows[_random.Next(windows.Count)];
                Dispatcher.UIThread.Post(() => randomWindow.SpawnBubbleAt(relX, relY, size),
                    DispatcherPriority.Background);
            }
        }

        private void SpawnBubbleAt(double relX, double relY, int size)
        {
            try
            {
                if (_screen == null) return;

                // Convert relative position to screen coordinates. WorkingArea excludes the panel,
                // and it is in PHYSICAL pixels - as is Window.Position - so the WPF original's
                // per-component DPI division is gone. The bubble's Width/Height are still DIPs,
                // hence the scaling on the half-size centring offset only.
                var area = _screen.WorkingArea;
                var scale = _screen.Scaling;
                var screenX = (int)Math.Round(area.X + relX * area.Width - size * scale / 2.0);
                var screenY = (int)Math.Round(area.Y + relY * area.Height - size * scale / 2.0);

                // Sound plays on pop (in StartPopping), not on spawn

                // Bubble is a separate window (doesn't block the video surface's rendering)
                var bubble = new CountBubble(_bubbleImage, size, screenX, screenY, _random,
                    PlayPopSound, this);
                _activeBubbles.Add(bubble);
                EnsureBubbleAnimTimer();
            }
            catch { }
        }

        /// <summary>Lazily start the shared animation timer once there are bubbles to animate.</summary>
        private void EnsureBubbleAnimTimer()
        {
            if (_bubbleAnimTimer != null) return;
            _bubbleAnimTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(BubbleAnimTickMs)
            };
            _bubbleAnimTimer.Tick += AnimateAllCountBubbles;
            _bubbleAnimTimer.Start();
        }

        private void QueueBubbleRestack()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || _videoEnded || _isCleaningUp) return;
                foreach (var bubble in _activeBubbles) bubble.RestackAbove(this);
            }, DispatcherPriority.Background);
        }

        /// <summary>Advances every active count bubble and reaps finished ones. Stops the timer
        /// when no bubbles remain (it restarts on the next spawn).</summary>
        private void AnimateAllCountBubbles(object? sender, EventArgs e)
        {
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                if (i >= _activeBubbles.Count) continue;
                var bubble = _activeBubbles[i];
                bubble.Tick(BubbleAnimTickMs);
                if (bubble.IsFinished)
                {
                    _activeBubbles.RemoveAt(i);
                    bubble.Dispose();
                }
            }

            if (_activeBubbles.Count == 0)
            {
                _bubbleAnimTimer?.Stop();
                _bubbleAnimTimer = null;
            }
        }

        /// <summary>
        /// One of the three pop samples, at (master * bubbles) ^ 1.5 - the WPF formula verbatim.
        /// Unseeded CoreAudio is a no-op that still returns, so a head with no audio backend just
        /// pops silently rather than stalling the animation tick that calls this.
        /// </summary>
        private void PlayPopSound()
        {
            var soundIndex = _random.Next(3);
            var masterVolume = CoreSettings.Current.MasterVolume / 100f;
            var bubblesVolume = CoreSettings.Current.BubblesVolume / 100f;

            try
            {
                var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var popPath = Path.Combine(soundsPath, popFiles[soundIndex]);

                var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5);
                CoreAudio.PlayOneShot(popPath, volume, "bubblecount-pop");
            }
            catch { }
        }

        private void OnVideoEnded()
        {
            if (_closed || _gameCompleted || _videoEnded || _isCleaningUp) return;
            _videoEnded = true;

            _safetyTimer?.Stop();
            _bubbleSpawnTimer?.Stop();

            // Mark all windows as ended
            foreach (var window in _allWindows.ToList())
            {
                window._videoEnded = true;
                window._bubbleSpawnTimer?.Stop();
            }
            // WPF paused the players: the windows are only HIDDEN for the result screen.
            try { _player?.SetPause(true); } catch { }
            foreach (var window in _allWindows) window._browser?.Pause();

            // Clear remaining bubbles on all windows (bubbles are separate windows now).
            // Dispose directly: the shared animation timer that would otherwise finish their
            // pop-out is about to stop, so close them now to avoid orphaned windows.
            foreach (var window in _allWindows.ToList())
            {
                foreach (var bubble in window._activeBubbles.ToArray())
                {
                    bubble.Dispose();
                }
                window._activeBubbles.Clear();
                window._bubbleAnimTimer?.Stop();
                window._bubbleAnimTimer = null;
            }

            // WPF :1399. The XP the game earns is awarded by the result window, not here.
            if (_isPrimary) CreditWatchTime();

            // Show result window (only from primary)
            if (_isPrimary)
            {
                ShowResultWindow();
            }
        }

        /// <summary>
        /// Hide the game and ask for the count. Verbatim WPF: the game windows are HIDDEN, not
        /// closed, so the result window's own multi-monitor set can be torn down independently and
        /// its answer still resolves this game through the shared completion callback.
        /// </summary>
        private void ShowResultWindow()
        {
            foreach (var window in _allWindows.ToList())
            {
                try { window.Hide(); } catch { }
            }

            BubbleCountResultWindow.ShowOnAllMonitors(
                _sharedBubbleCount,
                _strictMode,
                success =>
                {
                    _gameCompleted = true;
                    CloseAllWindows(success);
                });
        }

        /// <summary>WPF: Esc skips a non-strict game. Strict swallows the panic key and Alt+F4 while
        /// the global panic listener can stop the game, and falls open without one (panic key or Esc
        /// force-stops, LockCardWindow #875) - the rules MandatoryVideoScheduler.KeyAction owns.</summary>
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (_gameCompleted || _isCleaningUp) return;
            var s = CoreSettings.Current;
            switch (MandatoryVideoScheduler.KeyAction(_strictMode, e.Key.ToString(), e.KeyModifiers.HasFlag(KeyModifiers.Alt),
                s.PanicKeyEnabled, HotkeyNames.Canonical(s.PanicKey), s.PanicKeyEnabled && PanicListenerLive()))
            {
                case VideoKeyAction.Dismiss:
                    e.Handled = true;
                    _gameCompleted = true;
                    CloseAllWindows(false);
                    break;
                case VideoKeyAction.ForceStop:
                    e.Handled = true;
                    if (CoreEngine.BubbleCount is { } game) game.ForceCleanup(); else ForceCloseAll();
                    break;
                case VideoKeyAction.Swallow: e.Handled = true; break;
            }
        }

        private void CloseAllWindows(bool success)
        {
            lock (_cleanupLock)
            {
                if (_isCleaningUp) return;
                _isCleaningUp = true;
            }

            try
            {
                var primary = _allWindows.FirstOrDefault(w => w._isPrimary) ?? (_isPrimary ? this : null);
                primary?.CreditWatchTime();
                var started = System.Diagnostics.Stopwatch.StartNew();
                var stopped = StopPlayer();
                var windowsCopy = _allWindows.ToList();
                _allWindows.Clear(); System.Threading.Volatile.Write(ref _videoWindowCount, _allWindows.Count);
                foreach (var window in windowsCopy)
                {
                    try
                    {
                        window._safetyTimer?.Stop();
                        window._bubbleSpawnTimer?.Stop();
                        window.Close();
                    }
                    catch { }
                }

                // Preserve strict retry through a slow stop, but never strand the result.
                var generation = _completionGeneration;
                if (stopped.IsCompleted) _onComplete?.Invoke(success);
                else _ = CompleteAfterStop(stopped, TimeSpan.FromMilliseconds(Math.Max(0, 4000 - started.ElapsedMilliseconds)),
                    () => { if (generation == _completionGeneration) _onComplete?.Invoke(success); });
            }
            finally
            {
                lock (_cleanupLock)
                {
                    _isCleaningUp = false;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_isPrimary) CreditWatchTime();
            _allWindows.Remove(this);
            _closed = true;
            _browser?.Dispose();
            _browser = null;
            if (_isPrimary && !_gameCompleted && !_isCleaningUp) CloseAllWindows(false);
            _safetyTimer?.Stop();
            _safetyTimer = null;
            _bubbleSpawnTimer?.Stop();
            // Nulled, not just stopped: StartBubbleSpawning's 1500ms deferred start reads this
            // field and must become a no-op on a window that has already closed.
            _bubbleSpawnTimer = null;
            _bubbleAnimTimer?.Stop();
            _bubbleAnimTimer = null;

            foreach (var bubble in _activeBubbles)
            {
                bubble.Dispose();
            }
            _activeBubbles.Clear();

            _allWindows.Remove(this); System.Threading.Volatile.Write(ref _videoWindowCount, _allWindows.Count);

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Individual bubble for the counting game - its own window, so it never shares a visual tree
    /// with the video surface.
    ///
    /// WPF Windows/BubbleCountWindow.xaml.cs:1729 raises every new bubble to HWND_TOPMOST,
    /// above older bubbles and other topmost windows. Windows uses the same native raise here.
    /// X11 keeps its owner-relative restack. Both backends also make the bubble click-through.
    /// </summary>
    internal class CountBubble : IDisposable
    {
        private readonly Window _window;
        private readonly Image _imageElement;

        private readonly Action? _playSound;

        private double _scale = 0.1;
        private readonly double _targetScale = 1.0;
        private double _opacity = 1.0;
        private double _rotation = 0;
        private bool _isPopping = false;
        private bool _isDisposed = false;
        private double _lifeRemainingMs;

        /// <summary>True once the pop animation has fully faded out; the owning window
        /// removes and disposes finished bubbles on its shared animation tick.</summary>
        public bool IsFinished { get; private set; }

        public CountBubble(IImage? image, int size, int screenX, int screenY,
            Random random, Action? playSound, Window owner)
        {
            _playSound = playSound;
            _rotation = random.Next(360);

            _imageElement = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Source = image,
                RenderTransformOrigin = RelativePoint.Center
            };

            if (image == null)
            {
                // WPF drew this through a DrawingGroup/DrawingContext; Avalonia's DrawingImage
                // takes a GeometryDrawing directly, same ellipse, same stroke, same gradient.
                var gradientBrush = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(200, 255, 182, 193), 0),
                        new GradientStop(Color.FromArgb(100, 255, 105, 180), 1),
                    }
                };
                _imageElement.Source = new DrawingImage
                {
                    Drawing = new GeometryDrawing
                    {
                        Brush = gradientBrush,
                        Pen = new Pen(Brushes.White, 2),
                        Geometry = new EllipseGeometry(new Rect(5, 5, size - 10, size - 10)),
                    }
                };
            }

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(_scale, _scale));
            transformGroup.Children.Add(new RotateTransform(_rotation));
            _imageElement.RenderTransform = transformGroup;

            // Create separate window for bubble (doesn't share a visual tree with the video)
            _window = new Window
            {
                // WindowDecorations, not the SystemDecorations the .axaml files still use:
                // Avalonia 12 obsoleted the old property and the enum it took no longer resolves
                // from C#. Same meaning - no title bar, no border.
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                CanResize = false,
                IsHitTestVisible = false,
                Width = size,
                Height = size,
                Position = new PixelPoint(screenX, screenY),
                Content = _imageElement
            };

            _window.Show();
            BubbleCountWedgeWatchdog.Game.Register(_window);

            // IsHitTestVisible only silences Avalonia's own hit testing; the compositor would still
            // send the click here. This is the WS_EX_TRANSPARENT the original got implicitly.
            X11Overlay.SetClickThrough(_window, true);
            StackWindow(X11Overlay.BackendOf(_window.TryGetPlatformHandle()),
                () => X11Overlay.Raise(_window), () => RestackAbove(owner));

            // Lifespan - bubble stays for 1-1.5 seconds then pops. Driven by the owning
            // window's single shared animation timer (see BubbleCountWindow.AnimateAllCountBubbles)
            // rather than per-bubble timers, which previously meant ~2 DispatcherTimers per bubble.
            _lifeRemainingMs = 1000 + random.Next(500);
        }

        // WPF Windows/BubbleCountWindow.xaml.cs:1729: each new bubble heads the topmost band.
        internal static void StackWindow(OverlayBackend backend, Action raise, Action restack)
        {
            if (backend == OverlayBackend.Win32) raise();
            else if (backend == OverlayBackend.X11) restack();
        }

        // Native ordering is unavailable in headless tests; the seam lets them observe restacks.
        internal static Action<Window, Window> Restack = (bubble, owner) => X11Overlay.RestackAbove(bubble, owner);

        internal void RestackAbove(Window owner)
        {
            if (!_isDisposed) Restack(_window, owner);
        }

        /// <summary>
        /// Advance this bubble one frame. Called by the owning window's shared timer with the
        /// elapsed milliseconds since the last tick. Sets <see cref="IsFinished"/> when done.
        /// </summary>
        public void Tick(double dtMs)
        {
            if (_isDisposed) return;

            try
            {
                if (!_isPopping)
                {
                    _lifeRemainingMs -= dtMs;
                    if (_lifeRemainingMs <= 0) StartPopping();
                }

                if (_isPopping)
                {
                    _scale += 0.08;
                    _opacity -= 0.12;
                    _rotation += 5;

                    if (_opacity <= 0)
                    {
                        IsFinished = true;
                        return;
                    }
                }
                else
                {
                    if (_scale < _targetScale)
                    {
                        _scale = Math.Min(_targetScale, _scale + 0.1);
                    }
                    _rotation += 0.5;
                }

                _window.Opacity = Math.Max(0, _opacity);

                if (_imageElement.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
                {
                    if (tg.Children[0] is ScaleTransform st)
                    {
                        st.ScaleX = _scale;
                        st.ScaleY = _scale;
                    }
                    if (tg.Children[1] is RotateTransform rt)
                    {
                        rt.Angle = _rotation;
                    }
                }
            }
            catch { }
        }

        private void StartPopping()
        {
            if (_isPopping || _isDisposed) return;
            _isPopping = true;
            _playSound?.Invoke();
        }

        public void ForcePop()
        {
            if (_isDisposed) return;
            StartPopping();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try { _window.Close(); } catch { }
        }
    }
}
