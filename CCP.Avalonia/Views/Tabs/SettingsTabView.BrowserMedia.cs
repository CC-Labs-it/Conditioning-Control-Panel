using System;
using System.Text.Json;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        // WPF BrowserService.cs:332-457 and BrowserMediaService.cs:230-277, :301-375,
        // :491-543. This head has no interaction queue; the Home browser state feeds Takeover,
        // scheduled mandatory video and Bubble Count gates. The reporter is injected after navigation because WebHost
        // cannot inject at document creation, and can observe only the top document.
        internal bool BrowserMediaPlaying { get; private set; }
        internal bool BrowserMediaOwnsSlot { get; private set; }
        internal Func<DateTime> BrowserMediaClock = () => DateTime.UtcNow;
        private DateTime _browserMediaBeat;
        private DateTime _browserMediaEnded = DateTime.MinValue;
        private IDisposable? _browserTakeoverStop;
        private int _browserTakeoverStopGeneration, _browserTakeoverCap;
        private bool _browserTakeover;
        private bool _browserAutoPlayPending;
        private DispatcherTimer? _browserMediaWatchdog;
        private double _browserMediaDuration;
        private IDisposable? _browserMediaRecovery;
        private int _browserMediaRecoveryGeneration;
        private Action? _browserDeferredVideo;
        private bool _waitingForInteractionWindows;
        private BubbleCountScheduler? _earlierBubbleGame;
        private long _earlierBubbleGeneration;
        private IDisposable? _browserVideoRetry;
        private int _browserVideoRetryGeneration;
        internal static readonly TimeSpan BrowserVideoRetryPeriod = TimeSpan.FromMilliseconds(250);
        internal Func<Action, TimeSpan, IDisposable> ScheduleBrowserVideoRetry =
            (action, delay) => DispatcherTimer.RunOnce(action, delay);

        private bool EarlierBubbleIsQueued => _earlierBubbleGame is { } game
            && ReferenceEquals(game, CoreEngine.BubbleCount) && game.IsQueuedOrReplaying
            && game.QueuedGeneration == _earlierBubbleGeneration;

        internal bool BrowserVideoBlocksBubbleCount => _browserDeferredVideo != null && !EarlierBubbleIsQueued;

        internal bool DeferVideoForBrowser(Action replay)
        {
            if (!BrowserMediaOwnsSlot && _browserDeferredVideo == null) return false;
            if (_browserDeferredVideo == null)
            {
                _browserDeferredVideo = replay;
                if (CoreEngine.BubbleCount is { IsQueued: true } game)
                {
                    _earlierBubbleGame = game;
                    _earlierBubbleGeneration = game.QueuedGeneration;
                }
            }
            return true;
        }

        internal void CancelBrowserDeferredVideo()
        {
            _browserDeferredVideo = null;
            _earlierBubbleGame = null;
            _browserVideoRetryGeneration++;
            _browserVideoRetry?.Dispose();
            _browserVideoRetry = null;
            if (!_waitingForInteractionWindows) return;
            _waitingForInteractionWindows = false;
            Windows.LockCardWindow.AllClosed -= OnInteractionWindowsClosed;
            Windows.PopQuizWindow.AllClosed -= OnInteractionWindowsClosed;
        }

        // Post so other close handlers can open their held interaction before we recheck.
        private void OnInteractionWindowsClosed() => Dispatcher.UIThread.Post(TryReplayBrowserVideo);

        private void TryReplayBrowserVideo()
        {
            if (_browserDeferredVideo == null || BrowserMediaOwnsSlot) return;
            if (Windows.LockCardWindow.IsAnyOpen() || Windows.PopQuizWindow.IsAnyOpen())
            {
                if (!_waitingForInteractionWindows)
                {
                    _waitingForInteractionWindows = true;
                    Windows.LockCardWindow.AllClosed += OnInteractionWindowsClosed;
                    Windows.PopQuizWindow.AllClosed += OnInteractionWindowsClosed;
                }
                return;
            }
            if (EarlierBubbleIsQueued || CoreEngine.BubbleCount?.IsBusy == true
                || Windows.BubbleCountWindow.IsAnyOpen() || Windows.BubbleCountResultWindow.OpenWindows.Count > 0)
            {
                if (_browserVideoRetry == null)
                {
                    var generation = _browserVideoRetryGeneration;
                    _browserVideoRetry = ScheduleBrowserVideoRetry(() =>
                    {
                        if (generation != _browserVideoRetryGeneration) return;
                        _browserVideoRetry = null;
                        TryReplayBrowserVideo();
                    }, BrowserVideoRetryPeriod);
                }
                return;
            }
            var replay = _browserDeferredVideo;
            CancelBrowserDeferredVideo();
            replay();
        }
        // Like the pulse timer seam, tests step this without waiting for a long clip.
        internal Func<Action, TimeSpan, IDisposable> ScheduleBrowserMediaRecovery =
            (action, delay) => DispatcherTimer.RunOnce(action, delay);

        // WPF BrowserMediaService.ShouldDeferNewVideo: only fresh videos get the cool-off.
        internal bool BrowserMediaCooling => CoreSettings.Current.ProtectBrowserVideoPlayback
            && _browserMediaEnded != DateTime.MinValue
            && (BrowserMediaClock() - _browserMediaEnded).TotalSeconds < CoreSettings.Current.BrowserVideoGraceSeconds;

        // WPF BrowserMediaService.BeginTakeover: claim before navigation, with no queue.
        internal bool TryBeginBrowserTakeover()
        {
            if (BrowserMediaPlaying || BrowserMediaOwnsSlot || BrowserMediaCooling || _browserDeferredVideo != null
                || CoreEngine.Video?.IsPlaying == true || CoreEngine.BubbleCount?.IsBusy == true
                || Windows.BubbleCountWindow.IsAnyOpen() || Windows.BubbleCountResultWindow.OpenWindows.Count > 0
                || Windows.LockCardWindow.IsAnyOpen()
                || Windows.PopQuizWindow.IsAnyOpen()) return false;
            _browserTakeover = true;
            BrowserMediaOwnsSlot = true;
            _browserMediaBeat = BrowserMediaClock();
            _browserMediaWatchdog ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _browserMediaWatchdog.Tick -= BrowserMediaWatchdogTick;
            _browserMediaWatchdog.Tick += BrowserMediaWatchdogTick;
            _browserMediaWatchdog.Start();
            ArmBrowserMediaRecovery();
            return true;
        }

        internal void EndBrowserTakeover() => StopBrowserMediaTracking();

        internal void ArmBrowserAutoPlay() => _browserAutoPlayPending = true;
        internal void CancelBrowserAutoPlay() => _browserAutoPlayPending = false;

        internal void ReplaceBrowserTakeover()
        {
            if (!_browserTakeover) { CancelBrowserAutoPlay(); return; }
            _ = BrowserWebHost.InvokeScriptAsync(BrowserMediaRecoveryScript);
            StopBrowserMediaTracking();
        }

        private void OnBrowserTakeoverNavigation(Uri url, bool success)
        {
            if (!_browserAutoPlayPending) return;
            _browserAutoPlayPending = false;
            if (!success) { if (_browserTakeover) EndBrowserTakeover(); return; }
            _ = BrowserWebHost.InvokeScriptAsync(BrowserTakeoverScript);
        }

        // WPF MainWindow.Browser.cs:838: wait for the player, play, request fullscreen, exit on end.
        internal const string BrowserTakeoverScript = """
            (async function() {
                window._ccpTakeoverStopped = false;
                let video = document.querySelector('video');
                for (let i = 0; !video && i < 20; i++) {
                    await new Promise(r => setTimeout(r, 500));
                    if (window._ccpTakeoverStopped) return;
                    video = document.querySelector('video');
                }
                if (!video || window._ccpTakeoverStopped) return;
                const exit = () => { if (document.fullscreenElement && document.exitFullscreen)
                    document.exitFullscreen().catch(() => {}); };
                video.addEventListener('ended', exit, { once: true });
                video.addEventListener('dblclick', exit);
                let entered = false, reported = false;
                document.addEventListener('fullscreenchange', () => {
                    if (document.fullscreenElement || document.webkitFullscreenElement) { entered = true; return; }
                    if (!entered || reported) return;
                    reported = true;
                    try {
                        const msg = JSON.stringify({ type: 'ccpFsExit' });
                        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(msg);
                        else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg);
                    } catch (_) {}
                });
                const fullscreen = () => {
                    if (!window._ccpTakeoverStopped && video.requestFullscreen) {
                        const request = video.requestFullscreen();
                        if (request && request.catch) request.catch(() => {});
                    }
                };
                video.muted = false;
                video.play().then(fullscreen).catch(() => video.addEventListener('playing', fullscreen, { once: true }));
            })();
            """;

        private void StartBrowserMediaReporter() => _ = BrowserWebHost.InvokeScriptAsync(BrowserMediaReporter);

        internal void OnBrowserMediaMessage(string message)
        {
            try
            {
                // WebHost adapters can return the page string raw or JSON-quoted.
                if (message.TrimStart().StartsWith('"'))
                    message = JsonSerializer.Deserialize<string>(message) ?? "";
                using var json = JsonDocument.Parse(message);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)) return;
                if (type.GetString() == "ccpFsExit")
                {
                    _browserTakeover = false;
                    CancelBrowserTakeoverStop();
                    return;
                }
                if (type.GetString() != "ccpMedia" || !root.TryGetProperty("state", out var state)) return;
                switch (state.GetString())
                {
                    case "playing":
                        if (_browserTakeover && !BrowserMediaPlaying) _browserTakeoverCap = CoreSettings.Current.VideoMaxDurationSeconds;
                        // WPF BrowserMediaService.cs:248-257 tries once per user session.
                        // This head has no shared queue, so use its currently exposed owners.
                        if (!BrowserMediaPlaying && !_browserTakeover)
                        {
                            _browserMediaDuration = 0;
                            BrowserMediaOwnsSlot = CoreEngine.Video?.IsPlaying != true
                                && CoreEngine.BubbleCount?.IsBusy != true
                                && !Windows.BubbleCountWindow.IsAnyOpen()
                                && !Windows.LockCardWindow.IsAnyOpen() && !Windows.PopQuizWindow.IsAnyOpen();
                        }
                        if (MediaDuration(root) is double duration)
                        {
                            _browserMediaDuration = duration;
                            if (_browserTakeover) ArmBrowserTakeoverStop(duration);
                        }
                        BrowserMediaPlaying = true;
                        if (BrowserMediaOwnsSlot) ArmBrowserMediaRecovery();
                        _browserMediaBeat = BrowserMediaClock();
                        _browserMediaWatchdog ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                        _browserMediaWatchdog.Tick -= BrowserMediaWatchdogTick;
                        _browserMediaWatchdog.Tick += BrowserMediaWatchdogTick;
                        _browserMediaWatchdog.Start();
                        break;
                    case "progress":
                        if (BrowserMediaPlaying)
                        {
                            _browserMediaBeat = BrowserMediaClock();
                            if (_browserTakeover && root.TryGetProperty("played", out var played) && played.TryGetDouble(out var seconds)
                                && double.IsFinite(seconds) && VideoMaxCap.CapLeft(_browserTakeoverCap, seconds) == 0)
                            { ForceEndBrowserTakeover(); break; }
                            if (MediaDuration(root) is double reportedDuration && Math.Abs(reportedDuration - _browserMediaDuration) > 1)
                            {
                                _browserMediaDuration = reportedDuration;
                                if (_browserTakeover) ArmBrowserTakeoverStop(reportedDuration);
                            }
                        }
                        break;
                    case "stopped":
                        // WPF BrowserMediaService.cs:348: outgoing pagehide is not the new session ending.
                        if (_browserTakeover && !BrowserMediaPlaying && root.TryGetProperty("reason", out var reason)
                            && reason.GetString() == "navigated") { _browserMediaBeat = BrowserMediaClock(); break; }
                        StopBrowserMediaTracking();
                        break;
                }
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }

        private void BrowserMediaWatchdogTick(object? sender, EventArgs e) => CheckBrowserMediaHeartbeat();

        internal void CheckBrowserMediaHeartbeat()
        {
            var timeout = BrowserMediaPlaying ? 20 : 60;
            if ((BrowserMediaPlaying || BrowserMediaOwnsSlot) && (BrowserMediaClock() - _browserMediaBeat).TotalSeconds >= timeout)
            {
                if (_browserTakeover) ForceEndBrowserTakeover();
                else StopBrowserMediaTracking();
            }
        }

        // WPF BrowserMediaService.cs:436-450 and InteractionQueueService.cs:284-298:
        // known length + 15 s + 30 s, minimum 5 min; unknown length gets 3600 s + 30 s.
        private static double? MediaDuration(JsonElement root) =>
            root.TryGetProperty("dur", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var duration) && double.IsFinite(duration) && duration > 0 ? duration : null;

        // WPF BrowserMediaService.cs:459: end an autonomous clip at its duration plus 15 s.
        private void ArmBrowserTakeoverStop(double duration)
        {
            CancelBrowserTakeoverStop();
            var generation = _browserTakeoverStopGeneration;
            _browserTakeoverStop = ScheduleBrowserMediaRecovery(() =>
            {
                if (generation == _browserTakeoverStopGeneration && _browserTakeover)
                    ForceEndBrowserTakeover();
            }, TimeSpan.FromSeconds(Math.Min(duration + 15, 3600)));
        }

        private void CancelBrowserTakeoverStop()
        {
            _browserTakeoverStopGeneration++;
            _browserTakeoverStop?.Dispose();
            _browserTakeoverStop = null;
        }

        private void ForceEndBrowserTakeover()
        {
            try { _ = BrowserWebHost.InvokeScriptAsync(BrowserMediaRecoveryScript); }
            finally { StopBrowserMediaTracking(); }
        }

        private void ArmBrowserMediaRecovery()
        {
            CancelBrowserMediaRecovery();
            var seconds = _browserMediaDuration > 0 ? Math.Max(300, _browserMediaDuration + 45) : 3630;
            var delay = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds - 1));
            var generation = _browserMediaRecoveryGeneration;
            _browserMediaRecovery = ScheduleBrowserMediaRecovery(() =>
            {
                if (generation != _browserMediaRecoveryGeneration || !BrowserMediaOwnsSlot) return;
                // WPF InteractionQueueService.cs:471-472 -> BrowserMediaService.cs:400-414 ->
                // MainWindow.Browser.cs:981-984: stop media and leave fullscreen before releasing.
                _ = BrowserWebHost.InvokeScriptAsync(BrowserMediaRecoveryScript);
                StopBrowserMediaTracking();
            }, delay);
        }

        internal const string BrowserMediaRecoveryScript =
            "window._ccpForcedFs = false; window._ccpTakeoverStopped = true;" +
            "document.querySelectorAll('video,audio').forEach(v => { try { v.pause(); } catch (e) {} });" +
            "if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(() => {});";

        private void CancelBrowserMediaRecovery()
        {
            _browserMediaRecoveryGeneration++;
            _browserMediaRecovery?.Dispose();
            _browserMediaRecovery = null;
        }

        // WPF MainWindow.StartStop.cs:549-554: reset the queue without ending user playback.
        internal void ClearBrowserMediaClaim()
        {
            BrowserMediaOwnsSlot = false;
            _browserTakeover = false;
            _browserAutoPlayPending = false;
            CancelBrowserTakeoverStop();
            CancelBrowserMediaRecovery();
            CancelBrowserDeferredVideo();
            if (!BrowserMediaPlaying && _browserMediaWatchdog != null)
            {
                _browserMediaWatchdog.Stop();
                _browserMediaWatchdog.Tick -= BrowserMediaWatchdogTick;
                _browserMediaWatchdog = null;
            }
        }

        // WPF MainWindow.xaml.cs:2045,2113 and BrowserMediaService.cs:400-415.
        internal void StopBrowserMediaForPanic()
        {
            CancelBrowserDeferredVideo();
            ForceEndBrowserTakeover();
        }

        private void StopBrowserMediaTracking()
        {
            if (BrowserMediaPlaying || BrowserMediaOwnsSlot || _browserTakeover) _browserMediaEnded = BrowserMediaClock();
            CancelBrowserTakeoverStop();
            BrowserMediaPlaying = false;
            _browserTakeover = false;
            _browserAutoPlayPending = false;
            _browserMediaDuration = 0;
            BrowserMediaOwnsSlot = false;
            CancelBrowserMediaRecovery();
            if (_browserMediaWatchdog != null)
            {
                _browserMediaWatchdog.Stop();
                _browserMediaWatchdog.Tick -= BrowserMediaWatchdogTick;
                _browserMediaWatchdog = null;
            }
            TryReplayBrowserVideo();
        }

        // Adapted from WPF BrowserService.cs:340-457. The carrier uses the native WebHost
        // message bridge. WPF mutes the engine (BrowserService.cs:115), while WebHost mutes
        // elements. Treat our own mute as playback, keeping the 200 px preview filter.
        internal const string BrowserMediaReporter = """
            (function() {
                if (window.__ccpMediaBound) return;
                window.__ccpMediaBound = true;
                function post(o) {
                    try {
                        var s = JSON.stringify(o);
                        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(s);
                        else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(s);
                    } catch (_) {}
                }
                function real(m) {
                    return m && (!m.muted || window.__ccpMuted) && (m.tagName === 'AUDIO' || (m.offsetWidth || 0) >= 200);
                }
                function duration(m) {
                    var d = m.duration;
                    return (isFinite(d) && d > 0) ? d : 0;
                }
                function played(m) {
                    try { let n = 0; for (let i = 0; i < m.played.length; i++) n += m.played.end(i) - m.played.start(i); return n; }
                    catch (_) { return 0; }
                }
                var active = null, beat = null, pauseTimer = null;
                function stopBeat() { if (beat) { clearInterval(beat); beat = null; } }
                function report(reason) {
                    active = null; stopBeat();
                    post({ type: 'ccpMedia', state: 'stopped', reason: reason });
                }
                function cancelStop() { if (pauseTimer) { clearTimeout(pauseTimer); pauseTimer = null; } }
                function playing(e) {
                    var m = e.target;
                    if (!real(m)) return;
                    cancelStop(); active = m;
                    post({ type: 'ccpMedia', state: 'playing', dur: duration(m) });
                    if (!beat) beat = setInterval(function() {
                        if (active && !active.paused && !active.ended)
                            post({ type: 'ccpMedia', state: 'progress', dur: duration(active), played: played(active) });
                    }, 1000);
                }
                function ended(e) {
                    if (active && e.target !== active) return;
                    cancelStop(); report('ended');
                }
                function maybeStop(e) {
                    if (!active || e.target !== active) return;
                    cancelStop();
                    pauseTimer = setTimeout(function() {
                        pauseTimer = null;
                        if (active && active.paused && !active.ended) report('paused');
                    }, 2000);
                }
                function bind(m) {
                    if (!m || m.__ccpMediaHooked) return;
                    m.__ccpMediaHooked = true;
                    m.addEventListener('playing', playing);
                    m.addEventListener('ended', ended);
                    m.addEventListener('pause', maybeStop);
                    if (!m.paused && !m.ended && m.readyState >= 3) playing({ target: m });
                }
                function scan() {
                    try { document.querySelectorAll('video,audio').forEach(bind); } catch (_) {}
                }
                function init() {
                    scan();
                    try { new MutationObserver(scan).observe(document.documentElement, { childList: true, subtree: true }); } catch (_) {}
                    setInterval(scan, 3000);
                }
                if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
                else init();
                window.addEventListener('pagehide', function() { report('navigated'); });
            })();
            """;
    }
}
