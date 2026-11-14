using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services
{
    /// <summary>What the mandatory-video scheduler needs from a head: put a clip on the targeted
    /// screens, and take it down again. Called on the head's UI thread (ticks hop through
    /// <see cref="CoreDispatch"/>).</summary>
    public interface IMandatoryVideoHost
    {
        /// <summary>Open the full-screen video window(s) and play <paramref name="path"/>. The host calls
        /// <see cref="MandatoryVideoScheduler.End"/> at the clip's end or on Esc, and
        /// <see cref="MandatoryVideoScheduler.ForceCleanup"/> on the in-window panic key.</summary>
        void Show(string path, bool strict);
        /// <summary>WPF VideoService.cs:1982: visible, non-ghosted For You feed.</summary>
        bool FeedOwnsTheScreen => false;
        /// <summary>Use the host's presentation rule before the pre-roll as well as at Show.</summary>
        bool IsFloating(bool strict) => false;
        bool TryFloatingTrigger(bool userInitiated) => false;
        bool FloatingRunOpen => false;
        /// <summary>WPF VideoService.cs:2305-2313: wait while bubble count owns the screen.</summary>
        bool BubbleCountActive => false;
        void Freeze() { }
        void FinishFreeze(bool cancelled) { }
        /// <summary>Close every video window; returns the seconds of the clip that were watched.</summary>
        double CloseAll();
        /// <summary>WPF ShowMessage: the attention-check verdict over every screen for
        /// <paramref name="ms"/>, then <paramref name="then"/> on the UI thread.</summary>
        void ShowMessage(AttentionVerdict verdict, int ms, Action then);
    }

    /// <summary>What an ended clip's attention checks come to (WPF EndCurrentVideo :5955).</summary>
    public enum AttentionVerdict { None, Pass, Troll, Fail, Mercy }

    /// <summary>What a key press does in a mandatory-video window (WPF SetupStrictHandlers).</summary>
    public enum VideoKeyAction { None, Dismiss, ForceStop, Swallow }

    /// <summary>WPF VideoService.GraceDecision (#735): pause now, swallow the same keystroke's second
    /// handler, or fall through to the normal Esc/panic path.</summary>
    public enum GraceDecision { Pause, ConsumedDedup, FallThrough }

    /// <summary>
    /// The portable half of WPF <c>VideoService</c> (ConditioningControlPanel/Services/Video/VideoService.cs):
    /// the schedule (<c>ScheduleNext</c> :2952), the local pick (<c>GetNextVideo</c> :7997 /
    /// <c>RefillVideoQueues</c> :8337), the 1.3 s pre-roll (<c>PlayVideo</c> :3146), the two teardown
    /// doors (<c>Cleanup</c> :7934 re-arms, <c>ForceCleanup</c> :2670 does not), watch credit
    /// (<c>FinalizeWatchCredit</c> :7424) and the strict-key rules. Drawing is the head's
    /// <see cref="IMandatoryVideoHost"/>.
    /// Content-pack clips come through <see cref="PackVideos"/>.
    /// Remote clips come from the selected online channels through the device cache when the head sets <see cref="RemoteMedia"/>.
    /// ponytail: the duration filter
    /// (MetadataCache; the max-length cap in <see cref="Guard"/> still holds the max), browser-media defers and the
    /// interaction queue are WPF-head services; add each here when it reaches Core. The feed hold and
    /// cascade deferral are live; the cascade probe is seeded by the head.
    /// <para><b>Deliberate deviation:</b> a scheduled tick that finds an empty library re-arms the
    /// schedule. WPF returns from ContinueTriggerVideo (:2424) without ScheduleNext, so its schedule
    /// silently dies until the engine restarts; here a video added mid-session still plays
    /// (docs/avalonia-decisions.html).</para>
    /// </summary>
    public sealed class MandatoryVideoScheduler : IDisposable
    {
        /// <summary>WPF <c>SkipRetrySeconds</c>: a skipped tick retries this soon.</summary>
        public const double SkipRetrySeconds = 30;
        public static readonly TimeSpan PreRoll = TimeSpan.FromSeconds(1.3);
        public static readonly TimeSpan FreezeLead = TimeSpan.FromMilliseconds(800);

        public static readonly string[] SupportedVideoExtensions =
            { ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".m4v", ".mpg", ".mpeg", ".flv", ".ts" };

        private readonly IMandatoryVideoHost _host;
        private readonly FypFeedDefer _feedDefer;
        private readonly TimeProvider _time;
        private readonly Func<IReadOnlyList<string>> _library;
        private readonly Random _random = new();
        private Queue<string> _queue = new();
        private ITimer? _scheduler, _preroll, _bubbleWait, _cascadeDefer;
        private bool _deferIsReplay;   // the held request carries a verdict replay: dropping it must run AfterEnd
        private bool _waitEndsRun;     // the bubble-count wait carries a verdict replay: a failed replay must run AfterEnd

        /// <summary>WPF VideoService.cs:1916,2226: hold/coalesce for at most 90 s; recheck scheduled eligibility.</summary>
        public Func<bool>? CascadeIsRaining { get; set; }
        private volatile bool _running, _playing;
        private bool _videoFreeze;   // this clip played a Freeze: its end finishes it (Reset or cancel)
        private int _retryGeneration, _noVideosShown;

        /// <summary>The library came back empty for the first time this launch (WPF's "no videos"
        /// guidance dialog). Raised on the UI thread.</summary>
        public event Action? NoVideos;

        /// <summary>A clip is on screen (WPF VideoService.VideoStarted, VideoService.cs:3370).</summary>
        public event Action? VideoStarted;

        /// <summary>A playing clip has closed, including attention retries and forced cleanup.
        /// WPF VideoService.Cleanup:7950; fires once after the host closes and IsPlaying clears.</summary>
        public event Action? VideoEnded;

        /// <summary>The clip <see cref="VideoStarted"/> announced (WPF VideoService.LastVideoPath).</summary>
        public string? LastVideoPath { get; private set; }

        /// <summary>Replays forced this run (WPF _penalties); reset by <see cref="End"/>.</summary>
        public int Penalties { get; private set; }
        /// <summary>This clip's attention spawns and catches (WPF _spawned / _hits).</summary>
        public int AttentionSpawned { get; private set; }
        public int AttentionHits { get; private set; }

        /// <summary>A target batch went up (WPF SpawnTarget's _spawned++).</summary>
        public void NoteSpawn() => AttentionSpawned++;

        /// <summary>One target of a batch was caught: +15 XP (WPF SpawnTarget onHit).</summary>
        public void NoteHit()
        {
            AttentionHits++;
            _ = CoreHaptics.Service?.VideoTargetHitAsync();   // WPF VideoService.cs:5729
            CoreProgression.AddXP(AttentionHitXp, "Video");
        }

        public MandatoryVideoScheduler(IMandatoryVideoHost host, TimeProvider? time = null, Func<IReadOnlyList<string>>? library = null)
        {
            _host = host;
            _time = time ?? TimeProvider.System;
            _library = library ?? LocalLibrary;
            _feedDefer = new FypFeedDefer(_time, "VideoService", () => _host.FeedOwnsTheScreen);
        }

        /// <summary>The head may queue a trigger behind another fullscreen interaction.</summary>
        public Func<Action, bool>? DeferTrigger { get; set; }

        public bool IsRunning => _running;
        /// <summary>A video is in pre-roll or on screen (WPF <c>_videoPlaying</c>).</summary>
        public bool IsPlaying => _playing;
        public bool IsStrict { get; private set; }

        /// <summary>WPF ScheduleNext: 3600/perHour jittered 0.8-1.2x, never under 60 s.</summary>
        public static double NextIntervalSeconds(int perHour, double roll) =>
            Math.Max(60, 3600.0 / Math.Max(1, perHour) * (0.8 + roll * 0.4));

        /// <summary>WPF ShouldFillSecondaryMonitors (:2764) over the TARGETED screen count.</summary>
        public static bool ShouldFillSecondaryMonitors(int screenCount, bool fillAll) =>
            screenCount > 1 && (screenCount <= 2 || fillAll);

        /// <summary>WPF GetEffectiveVolume (:1630): master x video, 0-100.</summary>
        public static int EffectiveVolume(int master, int video) => (int)(master / 100.0 * (video / 100.0) * 100);

        /// <summary>WPF: +15 XP per caught target (SpawnTarget onHit).</summary>
        public const int AttentionHitXp = 15;

        /// <summary>WPF SetupAttention: AttentionDensity targets, or 1..density when randomized.</summary>
        public static int AttentionTargetCount(int density, bool randomize, Random r)
        {
            var max = Math.Max(1, density);
            return randomize ? r.Next(1, max + 1) : max;
        }

        /// <summary>WPF SetupAttention: spawn seconds in [3, 3 + max(1, dur-8)], sorted, at least 3 s apart
        /// (a 0-length clip counts as 60 s).</summary>
        public static List<double> AttentionSpawnTimes(int total, double durationSeconds, Random r)
        {
            var window = Math.Max(1, (durationSeconds > 0 ? durationSeconds : 60) - 8);
            var t = Enumerable.Range(0, total).Select(_ => 3 + r.NextDouble() * window).OrderBy(x => x).ToList();
            for (var i = 1; i < t.Count; i++) if (t[i] - t[i - 1] < 3) t[i] = t[i - 1] + 3;
            return t;
        }

        /// <summary>WPF EndCurrentVideo: every spawn caught passes (1 in 10 passes is trolled into a
        /// replay); any miss fails; with no spawn there is no verdict.</summary>
        public static AttentionVerdict Evaluate(bool enabled, int spawned, int hits, double roll) =>
            !enabled || spawned <= 0 ? AttentionVerdict.None
            : hits < spawned ? AttentionVerdict.Fail
            : roll < 0.1 ? AttentionVerdict.Troll : AttentionVerdict.Pass;

        /// <summary>WPF: a pass pays (replays + 1) x 50 + 200 XP.</summary>
        public static int AttentionPassXp(int penalties) => (penalties + 1) * 50 + 200;

        /// <summary>WPF VideoService.cs:3494/3861: a mandatory video decodes in software (the LibVLC
        /// hardware path can wedge on a white frame, #533/#537/#540) unless the user opted into GPU
        /// decode (VideoForceHardwareDecoding); this is the media option that forces it, or null.</summary>
        public static string? SoftwareDecodeOption(bool forceHardware) => forceHardware ? null : ":avcodec-hw=none";

        /// <summary>WPF NeedsBlurFill: bars (and so the blurred fill) only when the clip's aspect is
        /// more than 3% off the screen's.</summary>
        public static bool NeedsBlurFill(double videoAspect, double screenAspect) =>
            videoAspect > 0 && screenAspect > 0 && Math.Abs(videoAspect / screenAspect - 1.0) > 0.03;

        /// <summary>WPF AttentionTargetVisual.FormatTriggerText: 2 words stack, 4+ words split in two lines.</summary>
        public static string FormatTriggerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var w = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 2) return $"{w[0]}\n{w[1]}";
            if (w.Length >= 4) return string.Join(" ", w.Take(w.Length / 2)) + "\n" + string.Join(" ", w.Skip(w.Length / 2));
            return text;
        }

        public static bool IsSupportedVideoExtension(string path)
        {
            var ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path);
            return ext.Length > 0 && SupportedVideoExtensions.Contains(ext.ToLowerInvariant());
        }

        /// <summary>WPF SetupStrictHandlers minus the grace pause. Strict: the panic key, Alt+F4 and the
        /// System key do nothing - but only while a global panic listener is live to stop the video
        /// (<paramref name="panicListenerLive"/>: panic enabled AND its listener bound). Without one, the panic key and Esc force-stop it:
        /// strict must never trap the user behind a topmost full-screen window (LockCardWindow #875).
        /// Otherwise Esc dismisses (the run goes on) and the panic key force-stops.</summary>
        public static VideoKeyAction KeyAction(bool strict, string key, bool alt, bool panicEnabled, string? panicKey, bool panicListenerLive = true)
        {
            if (strict && !panicListenerLive && (key == "Escape" || key == panicKey))
                return VideoKeyAction.ForceStop;
            if (strict)
                return key == panicKey || key == "System" || (key == "F4" && alt) ? VideoKeyAction.Swallow : VideoKeyAction.None;
            if (key == "Escape") return VideoKeyAction.Dismiss;
            return panicEnabled && key == panicKey ? VideoKeyAction.ForceStop : VideoKeyAction.None;
        }

        /// <summary>WPF SetupStrictHandlers' Closing veto: a strict window refuses a user close only
        /// while the clip is genuinely playing (never a real teardown, or a strict window is left
        /// black and un-closable), and stands down once the app has wedged - a lock is only
        /// defensible while the app responds (#765).</summary>
        public static bool StrictVetoesClose(bool videoPlaying, bool cleaningUp, bool wedgeStallSeen)
            => videoPlaying && !cleaningUp && !wedgeStallSeen;

        // ---- grace pause (WPF #735) and the clip guards ----

        /// <summary>WPF GraceWindowSeconds: the pause resumes on its own after this.</summary>
        public const int GraceWindowSeconds = 60;
        /// <summary>WPF GraceDedupMs: one physical keystroke reaching two handlers pauses once.</summary>
        public const int GraceDedupMs = 200;

        /// <summary>WPF EvaluateGraceRequest: one grace pause per clip, only while it is really on
        /// screen; the press after it (or any press once it is spent) is the normal Esc/panic.</summary>
        public static GraceDecision EvaluateGrace(bool videoPlaying, bool cleaningUp, bool alreadyPaused, bool consumed,
            double msSinceLastGraceAction, int dedupMs = GraceDedupMs)
        {
            if (msSinceLastGraceAction < dedupMs) return GraceDecision.ConsumedDedup;
            if (!videoPlaying || cleaningUp || alreadyPaused || consumed) return GraceDecision.FallThrough;
            return GraceDecision.Pause;
        }

        /// <summary>WPF GraceSecondsRemaining: the countdown rounds up, so "60" shows for a full second.</summary>
        public static int GraceSecondsRemaining(double elapsedSeconds, int windowSeconds = GraceWindowSeconds)
            => (int)Math.Max(0, Math.Ceiling(windowSeconds - elapsedSeconds));

        /// <summary>WPF SetupStrictHandlers: whether this press first tries the grace pause, and if so
        /// whether it counts as the panic key (null = not a grace key). Strict: only Esc, only with a
        /// panic key that is not Esc; never when the strict fall-open force-stop owns the key.
        /// Otherwise Esc (as the panic key when it is bound to Esc) and the panic key.</summary>
        public static bool? GraceKey(bool strict, string key, bool panicEnabled, string? panicKey, bool panicListenerLive = true)
        {
            var escIsPanic = Safety.PanicPolicy.EscapeIsThePanicKey(panicEnabled, panicKey);
            if (strict)
                return panicListenerLive && key == "Escape" && panicEnabled && !escIsPanic ? false : null;
            if (key == "Escape") return escIsPanic;
            return panicEnabled && key == panicKey ? true : null;
        }

        /// <summary>WPF VoutGraceMs: no frame this long after Play is a dead output.</summary>
        public const double NoFrameSeconds = 8;
        /// <summary>WPF RemoteVoutGraceMs: remote clips have longer to produce their first frame.</summary>
        public const double RemoteNoFrameSeconds = 20;
        /// <summary>WPF VoutLostGraceMs: output that was there and stays gone this long is lost.</summary>
        public const double OutputLostSeconds = 5;
        /// <summary>WPF MaxVideoFallbackSeconds: the cap when the clip never reports a length.</summary>
        public const double FallbackSeconds = 600;

        /// <summary>WPF's clip guards, on unpaused clocks (a grace pause freezes them, as WPF re-phases
        /// its timers). <paramref name="sinceShow"/> counts from the window opening,
        /// <paramref name="played"/> from the first frame (from the opening for a clip with no video
        /// track, which WPF lets play out). "no frame": no output 8 s after Play locally, 20 s for a
        /// remote clip (VideoService.cs:6804-6809,8097-8100); "output lost": frames stopped for 5 s
        /// mid-clip (mid-play vout loss). See <see cref="GuardHeals"/>. "max length" (VideoMaxDurationSeconds
        /// cap), "overran" (length + 5 s safety timer, or 600 s with no length) end the clip. Null = fine.</summary>
        public static string? Guard(double sinceShow, double played, bool framed, bool hasVideo, double lengthSeconds,
            double sinceFrame, int maxSeconds, double capSpentBefore = 0, double? capPlayed = null, bool remote = false)
        {
            if (!framed && (hasVideo || remote)) return sinceShow >= (remote ? RemoteNoFrameSeconds : NoFrameSeconds) ? "no frame" : null;
            if (VideoMaxCap.CapLeft(maxSeconds, capSpentBefore + (capPlayed ?? played)) <= 0) return "max length";
            if (lengthSeconds > 0 ? played >= lengthSeconds + 5 : played >= FallbackSeconds) return "overran";
            return framed && sinceFrame >= OutputLostSeconds ? "output lost" : null;
        }

        /// <summary>WPF VoutWatchdogFire / VoutMidPlayTick: a dead or lost output replays a local clip
        /// once; a remote clip ends (VideoService.cs:6872-6885,6972-6982). After that (or for any other
        /// guard) the clip ends like a dismiss.</summary>
        public static bool GuardHeals(string why, bool healUsed, bool remote = false) =>
            !remote && !healUsed && why is "no frame" or "output lost";

        /// <summary>WPF PlayVideo(isVoutRetry): the replayed clip starts its attention checks afresh.</summary>
        public void NoteReplay() => AttentionSpawned = AttentionHits = 0;

        public void Start()
        {
            if (_running) return;
            _running = true;
            KickRemotePrefetch();
            ScheduleNext();
            Log.Information("VideoService started");
        }

        /// <summary>WPF Stop: every caller is a live stop (engine stop, panic, the card toggle).</summary>
        public void Stop()
        {
            _running = false;
            _feedDefer.Reset();
            _retryGeneration++;   // WPF CancelPendingRetry: a verdict message must not replay after a stop
            Dispose(ref _scheduler);
            ForceCleanup();
            Log.Debug("VideoService stopped");
        }

        public void Dispose() => Stop();

        private void ScheduleNext(double? retrySeconds = null)
        {
            var s = CoreSettings.Current;
            if (!_running || !s.MandatoryVideosEnabled) return;
            var secs = retrySeconds ?? NextIntervalSeconds(s.VideosPerHour, _random.NextDouble());
            Dispose(ref _scheduler);
            ITimer? created = null;
            created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() => Tick(mine)); },
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _scheduler = created;
            created.Change(TimeSpan.FromSeconds(secs), Timeout.InfiniteTimeSpan);
        }

        private void Tick(ITimer? firedBy)
        {
            if (!_running || !ReferenceEquals(firedBy, _scheduler)) return;
            Dispose(ref _scheduler);
            try
            {
                // WPF VideoService :3007: a do-not-disturb app in front reschedules, never drops.
                if (!_playing && Services.UI.DndGuard.ShouldSuppressVideos())
                {
                    Services.UI.DndGuard.LogSuppressionThrottled("scheduled video");
                    ScheduleNext(SkipRetrySeconds);
                }
                else if (!_playing && !Trigger(scheduled: true)) ScheduleNext();
            }
            catch (Exception ex)
            {
                // WPF #388: a throwing trigger must not end the session's videos.
                Log.Error(ex, "VideoService: scheduler tick failed - re-arming scheduler");
                _playing = false;
                ScheduleNext();
            }
        }

        // WPF VideoService.cs:8197: never hand an uncached Scrolller URL to the player.
        // Resolve again after pre-roll because the licence or cache can change in the meantime.
        internal Func<string, string?> CachedScrolllerPath { get; set; } = ScrolllerDiskCache.TryGetPath;

        private string? PlaybackPath(string path) => !ScrolllerPolicy.IsScrolllerUrl(path) ? path
            : ScrolllerPolicy.Enabled ? CachedScrolllerPath(path) : null;

        /// <summary>WPF TriggerVideo -> PlayVideo: pick a clip, stop the flash, and show it after the
        /// pre-roll. False when nothing was started (no clip, or one is already playing).</summary>
        public bool TriggerStartup()
        {
            var s = CoreSettings.Current;
            var path = s.StartupVideoPath;
            return Trigger(s.StrictLockEnabled, !string.IsNullOrEmpty(path) && System.IO.File.Exists(path) ? path : null,
                silentIfEmpty: true, freezeBeforePreroll: !s.AttentionChecksEnabled);
        }

        /// <summary>WPF TriggerVideo -> PlayVideo for every other caller: pick a clip, stop the flash, and show it
        /// after the pre-roll. True also means accepted/held behind a cascade, the feed or a bubble-count round;
        /// false means refused (no clip, or one is already playing).
        /// <paramref name="path"/> plays that clip instead of a pick (WPF PlaySpecificVideo).
        /// Scheduled ticks pass <paramref name="scheduled"/> so a cascade-held replay rechecks feature enablement.</summary>
        public bool Trigger(bool? strictOverride = null, string? path = null, bool userInitiated = false, bool userEarned = false,
            bool scheduled = false, bool silentIfEmpty = false, bool freezeBeforePreroll = false)
        {
            if (DeferTrigger?.Invoke(() => Trigger(strictOverride, path)) == true) return true;
            if (_playing)
                return _host.IsFloating(IsStrict) && _host.IsFloating(strictOverride ?? CoreSettings.Current.StrictLockEnabled)
                    && _host.TryFloatingTrigger(userInitiated);
            // WPF VideoService.cs:2226: the cascade guard comes before the feed guard.
            if (_cascadeDefer != null) return true; // WPF: coalesce, retain the first request
            if (CascadeIsRaining?.Invoke() == true)
            {
                var deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(90);
                ITimer? pending = null;
                pending = _time.CreateTimer(_ => CoreDispatch.Post(() =>
                {
                    if (!ReferenceEquals(pending, _cascadeDefer)) return;
                    var replay = _deferIsReplay;
                    try
                    {
                        // WPF ChaosGifCascadeOverlay.cs:173-182: expire only while it still rains;
                        // a poll that finds the rain cleared replays, even at the deadline.
                        var raining = CascadeIsRaining?.Invoke() == true;
                        if (raining && _time.GetUtcNow() < deadline)
                        {
                            pending!.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
                            return;
                        }
                        if (raining || (scheduled && (!_running || !CoreSettings.Current.MandatoryVideosEnabled)))
                        {
                            if (DropDefer()) AfterEnd();
                            else if (_running) ScheduleNext();
                            return;
                        }
                        replay = DropDefer();
                        if (!Trigger(strictOverride, path, userInitiated, userEarned, scheduled) && !_playing)
                        {
                            if (replay) AfterEnd();
                            else if (_running) ScheduleNext(); // preserve the base empty-library re-arm
                        }
                    }
                    catch (Exception ex)
                    {
                        // WPF ChaosGifCascadeOverlay.cs:170-189: failed held actions give up safely.
                        Log.Error(ex, "VideoService: cascade replay failed");
                        replay |= DropDefer(); // also cancel a probe failure before the hold was cleared
                        _playing = false;
                        IsStrict = false;
                        if (replay) AfterEnd();
                        else if (_running) ScheduleNext();
                    }
                }), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _cascadeDefer = pending;
                // ponytail: the existing head exposes IsRaining, not WPF RunWhenClear. Poll
                // off-thread, dispatch the replay; keep one absolute 90 s ceiling per request.
                pending.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
                return true;
            }
            // WPF VideoService.cs:2270-2293: earned and specific clips bypass only the feed hold.
            if (path == null && !userEarned && _host.FeedOwnsTheScreen)
            {
                if (userInitiated)
                    CoreProgram.Notify(Loc.Get("video_toast_feed_has_the_screen"), "Info", TimeSpan.FromSeconds(6));
                _feedDefer.Defer(() =>
                {
                    // WPF :2048: stop and live disable invalidate the held trigger.
                    if (!_running || !CoreSettings.Current.MandatoryVideosEnabled) return;
                    try
                    {
                        if (!Trigger(strictOverride)) Rearm();
                    }
                    catch (Exception ex)
                    {
                        // A replay that throws (unreadable library) must not leave the schedule dead (#3776).
                        Log.Error(ex, "VideoService: feed replay failed - re-arming scheduler");
                        if (!_playing) Rearm();
                    }
                }, Rearm);
                // Keep the base's empty-library rearm policy when a hold expires.
                void Rearm() { if (_scheduler == null && !_playing) ScheduleNext(); }
                return true;
            }
            // WPF InteractionQueueService.cs:108-121: the first queued request owns the handoff too.
            if (_bubbleWait != null) return true;
            // WPF VideoService.cs:2305-2313 queues one video before picking or taking effects.
            if (_host.BubbleCountActive)
            {
                WaitForBubbleCount(strictOverride, path, _running);
                return true;
            }
            var specific = path != null;   // WPF PlaySpecificVideo freezes even behind a bubble count
            path ??= PickNext();
            if (path == null)
            {
                Log.Warning("VideoService: no videos found in the library");
                // WPF #1124: the guidance dialog once per launch, never per tick.
                if (!silentIfEmpty && Interlocked.Exchange(ref _noVideosShown, 1) == 0) NoVideos?.Invoke();
                return false;
            }
            // WPF PlayVideo :3044: try one fresh pick before ending a refused run.
            if (PlaybackPath(path) == null)
            {
                path = PickNext();
                if (path == null || PlaybackPath(path) == null) return false;
            }
            // WPF TriggerVideo :2479-2485 and PlaySpecificVideo :2587: every clip gets the Freeze, not only the
            // startup one, unless attention checks (or, for a picked clip, the bubble-count game) need alert
            // input. Only on a head with a Freeze surface (CoreSubliminal.BambiFreezeProvider).
            freezeBeforePreroll |= CoreSubliminal.BambiFreezeProvider != null && !CoreSettings.Current.AttentionChecksEnabled &&
                (specific || !(CoreEngine.BubbleCount?.IsBusy ?? false));
            IsStrict = strictOverride ?? CoreSettings.Current.StrictLockEnabled;
            _playing = true;
            _retryGeneration++;   // WPF PlayVideo CancelPendingRetry: a new clip retires any pending verdict replay
            AttentionSpawned = AttentionHits = 0;
            // WPF VideoService.cs:2587-2630: Freeze, 800 ms, then PlayVideo's 1.3 s pre-roll.
            _videoFreeze = freezeBeforePreroll;
            if (freezeBeforePreroll)
            {
                // A failing Freeze surface never costs the clip.
                try { _host.Freeze(); }
                catch (Exception ex) { Log.Warning(ex, "VideoService: Freeze callback failed"); }
            }
            // WPF b6ff63fbd: only full-screen playback clears flashes, including a Test press.
            else if (!_host.IsFloating(IsStrict)) CoreFlash.Stop();
            var strict = IsStrict;
            Dispose(ref _preroll);
            ITimer? created = null;
            created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() =>
            {
                // WPF ShouldStartAfterPreroll: only the run that armed it, and only while still live.
                if (!ReferenceEquals(mine, _preroll) || !_playing) return;
                if (freezeBeforePreroll)
                {
                    freezeBeforePreroll = false;
                    // WPF PlayVideo prologue (VideoService.cs:3127): the floating rule holds after the freeze too.
                    if (!_host.IsFloating(strict)) CoreFlash.Stop();
                    mine!.Change(PreRoll, Timeout.InfiniteTimeSpan);
                    return;
                }
                Dispose(ref _preroll);
                var playbackPath = PlaybackPath(path);
                if (playbackPath == null) { End(); return; }
                // WPF StartVideoPlayback: App.Bubbles?.PauseAndClear() until the run ends, except floating (VideoFlashRule.ClearsBubbles)
                if (!_host.IsFloating(strict)) CoreBubbles.Pause();
                CoreDiscord.Service?.SetVideoActivity(); // WPF StartVideoPlayback:3133
                // WPF StartVideoPlayback: presentation can change during the pre-roll.
                if (!_host.IsFloating(strict) && CoreFlash.IsRunning) CoreFlash.Stop();
                var generation = _retryGeneration;
                ShowingPath = path;   // the host keeps the source URL identity when it plays a cached file
                try { _host.Show(playbackPath, strict); }
                catch (Exception ex) { Log.Error(ex, "VideoService: show failed"); if (_playing && generation == _retryGeneration) End(); return; }
                // WPF VideoService.cs:3218-3225,3404-3410: a refused or replaced clip never starts its hooks.
                if (!_playing || generation != _retryGeneration) return;
                LastVideoPath = path;
                try { VideoStarted?.Invoke(); }
                catch (Exception ex) { Log.Debug("VideoStarted handler failed: {Error}", ex.Message); }
                if (!_playing || generation != _retryGeneration) return;
                // WPF VideoService.cs:3372: background vibe and the clip's funscript, once on screen.
                _ = CoreHaptics.Service?.StartVideoBackgroundVibeAsync();
                if (!_playing || generation != _retryGeneration) return;
                try { CoreHaptics.Service?.FunScript.OnVideoStarted(path); }
                catch (Exception ex) { Log.Debug("FunScript start hook failed: {Error}", ex.Message); }
            }); }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _preroll = created;
            created.Change(freezeBeforePreroll ? FreezeLead : PreRoll, Timeout.InfiniteTimeSpan);
            Log.Information("VideoService: playing {File} (strict={Strict})", Path.GetFileName(path), strict);
            return true;
        }

        /// <summary>WPF Cleanup: the natural end or an Esc dismiss. The flash resumes and, while the
        /// engine runs, the next video is scheduled.</summary>
        public void End()
        {
            if (!Finish()) return;
            AfterEnd();
        }

        private void AfterEnd()
        {
            Penalties = 0;
            CoreBubbles.Resume();   // WPF Cleanup: App.Bubbles?.Resume() - not before a verdict replay
            if (_running && CoreSettings.Current.FlashEnabled) CoreFlash.Start();
            else CoreDiscord.Service?.SetIdleActivity(); // WPF Cleanup:7962
            if (_running) ScheduleNext();
        }

        /// <summary>True only during a rendered clip's successful natural-end teardown, never a verdict retry.</summary>
        public bool SoftExit { get; private set; }

        /// <summary>WPF EndCurrentVideo: the clip played to its end. Scores the attention checks
        /// (pass XP and achievements); a fail or a troll closes the clip, shows the verdict and plays
        /// a fresh clip with the same strictness, until the MercyAfterFails-th replay earns mercy
        /// (MercySystemEnabled), which ends the run like <see cref="End"/>.
        /// Pass/fail reach achievements (and the head's Chaster "attention" note) through
        /// <see cref="CoreProgression.TrackAttentionCheck"/> (the head's provider also takes the
        /// Trainer's -25 XP); the Chaster "video" note is the head's, it knows whether a frame showed.</summary>
        public void Ended(bool renderedFrame = false)
        {
            if (!_playing) return;
            var s = CoreSettings.Current;
            var verdict = Evaluate(s.AttentionChecksEnabled, AttentionSpawned, AttentionHits, _random.NextDouble());
            if (verdict != AttentionVerdict.None)
                Log.Information("Attention result: {Hits}/{Spawned} = {Verdict} (replays {Penalties})", AttentionHits, AttentionSpawned, verdict, Penalties);
            if (verdict is AttentionVerdict.Pass or AttentionVerdict.Troll)
            {
                CoreProgression.AddXP(AttentionPassXp(Penalties), "Video");
                CoreProgression.TrackAttentionCheck(true);
            }
            else if (verdict == AttentionVerdict.Fail) CoreProgression.TrackAttentionCheck(false);
            if (verdict is not (AttentionVerdict.Troll or AttentionVerdict.Fail))
            {
                SoftExit = renderedFrame;
                try { End(); }
                finally { SoftExit = false; }
                return;
            }

            // WPF VideoService.cs:5883-5917: score first; off ends an ordinary video without a retry card.
            if (!Video.MandatoryVideoPresentation.ReplaysAfterCheck(
                    s.MandatoryVideoReplayAfterFailedCheck, IsStrict, LockdownService.Current?.IsActive == true))
            {
                Log.Information("VideoService: attention {Verdict} - no replay (setting off)", verdict);
                End();
                return;
            }

            if (!Video.MandatoryVideoPresentation.ReplaysAfterCheck(s.MandatoryVideoReplayAfterFailedCheck,
                IsStrict, LockdownService.Current?.IsActive == true)) { End(); return; }

            Penalties++;
            var mercy = Penalties >= s.MercyAfterFails && s.MercySystemEnabled;   // #1145: 2..10, default 3
            var strict = IsStrict;
            var gen = ++_retryGeneration;
            Finish();
            _host.ShowMessage(mercy ? AttentionVerdict.Mercy : verdict, mercy ? 2500 : 2000, () =>
            {
                if (gen != _retryGeneration) return;   // stopped or replaced while the message was up
                if (mercy) { AfterEnd(); return; }
                // WPF VideoService.cs:5964-5965: retry directly, retaining strictness and the prior clip fallback.
                // An attention retry is part of the current run, so it must not enter the feed hold.
                var retryPath = PickNext() ?? LastVideoPath;
                if (retryPath == null || !Trigger(strict, retryPath)) AfterEnd();
                else if (_cascadeDefer != null && !_playing) _deferIsReplay = true;   // held behind GIF rain
                // WPF VideoService.cs:7930-7968: a failed replay must finish the run, even after queueing.
                else if (_bubbleWait != null && !_playing) _waitEndsRun = true;
            });
        }

        private void WaitForBubbleCount(bool? strict, string? path, bool scheduled)
        {
            ITimer? created = null;
            created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() =>
            {
                if (!ReferenceEquals(mine, _bubbleWait)) return;
                Dispose(ref _bubbleWait);
                var endsRun = _waitEndsRun;
                _waitEndsRun = false;
                try
                {
                    if (scheduled && (!_running || !CoreSettings.Current.MandatoryVideosEnabled))
                    {
                        if (endsRun) AfterEnd();
                        return;
                    }
                    if (_host.BubbleCountActive)
                    {
                        WaitForBubbleCount(strict, path, scheduled);
                        _waitEndsRun = endsRun;
                    }
                    else if (!Trigger(strict, path, scheduled: scheduled))
                    {
                        if (endsRun) AfterEnd();
                        else if (scheduled) ScheduleNext();
                    }
                }
                catch (Exception ex)
                {
                    // WPF VideoService.cs:2356-2363,2990-2998: contain selection/trigger failures.
                    Log.Error(ex, "VideoService: deferred trigger failed - re-arming scheduled playback");
                    _playing = false;
                    if (endsRun) AfterEnd();
                    else if (scheduled) ScheduleNext();
                }
            }); }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _bubbleWait = created;
            created.Change(BubbleCountScheduler.QueuePoll, Timeout.InfiniteTimeSpan);
        }

        /// <summary>WPF ForceCleanup: close without scheduling a replacement.</summary>
        public void ForceCleanup()
        {
            _feedDefer.Reset(); // A panic cannot leave a held trigger behind.
            Dispose(ref _bubbleWait);
            _waitEndsRun = false;
            var heldReplay = DropDefer();
            _retryGeneration++;
            // Deviation: WPF leaves bubbles paused here (its panic stops the engine too); this head's
            // fall-open panic force-stops the clip alone, and must not leave the field held forever.
            if (!Finish(cancelled: true))
            {
                // Also drop a fading curtain while idle; a host failure must not interrupt panic.
                try { _host.CloseAll(); }
                catch (Exception ex) { Log.Warning(ex, "VideoService: close failed"); }
                if (!heldReplay) return;
            }
            // Deviation: WPF ForceCleanup leaves the presence on "Watching"; a closed clip is not watched.
            CoreDiscord.Service?.SetIdleActivity();
            if (_running) CoreBubbles.Resume();   // Stop() clears _running first: no resume after an engine stop
        }

        /// <summary>Cancels a held cascade request; true when it carried a verdict replay.</summary>
        private bool DropDefer()
        {
            var replay = _deferIsReplay && _cascadeDefer != null;
            _deferIsReplay = false;
            Dispose(ref _cascadeDefer);
            return replay;
        }

        private bool Finish(bool cancelled = false)
        {
            Dispose(ref _preroll);
            if (!_playing) { PackVideos?.CleanupTemps(); return false; }
            _playing = false;
            IsStrict = false;
            ShowingPath = null;
            double watched = 0;
            try { watched = _host.CloseAll(); }
            catch (Exception ex) { Log.Warning(ex, "VideoService: close failed"); }
            PackVideos?.CleanupTemps(); // The host has stopped and disposed its players.
            if (_videoFreeze)
            {
                _videoFreeze = false;
                try { _host.FinishFreeze(cancelled); }
                catch (Exception ex) { Log.Warning(ex, "Video Freeze cleanup failed"); }
            }
            if (watched >= 1.0) CoreProgression.TrackVideoWatched(watched);
            // WPF CloseAll (VideoService.cs:7916): every teardown path stops the vibe and the funscript.
            _ = CoreHaptics.Service?.StopVideoBackgroundVibeAsync();
            try { CoreHaptics.Service?.FunScript.OnVideoStopped(); }
            catch (Exception ex) { Log.Debug("FunScript stop hook failed: {Error}", ex.Message); }
            try { VideoEnded?.Invoke(); }
            catch (Exception ex) { Log.Debug("VideoEnded handler failed: {Error}", ex.Message); }
            return true;
        }

        /// <summary>WPF VideoService.ReloadAssets (VideoService.cs:1583-1592): drop both dealt queues so the
        /// next pick re-reads the enabled library (an untick or preset switch takes effect on the very next
        /// video, #130), then sweep the pack decrypts.</summary>
        public void ReloadAssets()
        {
            RefreshVideosPath();
            PackVideos?.CleanupTemps();
        }

        /// <summary>WPF VideoService.RefreshVideosPath (VideoService.cs:1570-1577), the asset-import refresh:
        /// drop both dealt queues but keep every decrypt, since one may still be playing.</summary>
        public void RefreshVideosPath()
        {
            _queue = new Queue<string>();
            PackVideos?.ResetShuffle();
        }

        /// <summary>WPF GetNextVideo, local half: a shuffled queue refilled when it runs dry.</summary>
        internal string? PickNext()
        {
            if (_queue.Count == 0)
            {
                var enabled = _library();
                var kept = KeepByLength(enabled, CoreSettings.Current.VideoMinDurationSeconds,
                    CoreSettings.Current.VideoMaxDurationSeconds, DurationOf);
                LastFunnelEnabled = enabled.Count;   // WPF _lastFunnelEnabled / _lastFunnelDuration (#1352)
                LastFunnelDuration = kept.Count;
                _queue = new Queue<string>(kept.OrderBy(_ => _random.Next()));
            }
            // WPF GetNextVideo (VideoService.cs:7981-8005): try the ready remote pool first,
            // with the selected source and share; an empty pool falls back to local/pack.
            var s = CoreSettings.Current;
            var localEmpty = _queue.Count == 0 && (PackVideos?.Count ?? 0) == 0;
            KickRemotePrefetch();
            if (RemoteAllowed(s) && VideoSelectionRules.WantsRemote(localEmpty, s.MediaSource, s.RemoteMediaRatio, _random)
                && TakeRemote() is { } remote) { KickRemotePrefetch(); return remote; }
            // WPF GetNextVideo (VideoService.cs:8015-8049): active pack clips join the local pick,
            // weighted by count; a failed decrypt falls back to the local queue. Pack clips skip the
            // length filter (PackFileEntry carries no duration).
            if (PackVideos?.TryDraw(_queue.Count, remaining: true) is { } pack) return pack;
            return _queue.Count > 0 ? _queue.Dequeue() : null;
        }

        /// <summary>The content-pack clips mixed in (WPF _packVideoQueue); null = local only.</summary>
        public PackMediaPool? PackVideos { get; set; }

        /// <summary>The picked source of the clip being shown (a Scrolller URL for a remote clip, while the
        /// host plays its cached file); null when nothing plays. The host keys remote rules and the credit on it.</summary>
        public string? ShowingPath { get; private set; }

        // WPF VideoService.cs:8139-8155, 8222-8270, 8323-8368. The scheduler owns the "videos" tenant
        // and never waits for the network on a trigger: a fetch (Scrolller list API, at most one in
        // flight, only when fewer than 4 are queued) runs on Start and after each pick. A source or
        // channel change drops the buffer and any in-flight response before it is used.
        /// <summary>The head plays remote clips (consent, the online source and the licence still gate each one).</summary>
        public bool RemoteMedia { get; set; }
        private readonly object _remoteLock = new();
        private readonly List<string> _remoteQueue = new();
        private string _remoteSelection = "";
        private int _remoteInFlight;
        internal System.Threading.Tasks.Task? RemoteTask { get; private set; }
        internal Func<CancellationToken, System.Threading.Tasks.Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> FetchRemote { get; set; } =
            ct => FypOnlineCoordinator.For("videos", RemoteChannels, FeedMediaKind.Video).FetchBatchAsync(ct);
        internal Func<string, bool> MayFetchRemote { get; set; } = ScrolllerPolicyService.MayFetchFresh;
        internal Func<string, CancellationToken, System.Threading.Tasks.Task<string?>> DownloadRemote { get; set; } =
            (url, ct) => ScrolllerDiskCache.GetOrFetchAsync(url, ScrolllerItemIndex.MetaFor(url), ct);

        private bool CachedRemote(string url) => CachedScrolllerPath(url) != null;
        private static IReadOnlyList<string> RemoteChannels() => FypOnlineCoordinator.ResolveChannels(
            CoreSettings.Current.FypOnlineNiches, CoreSettings.Current.FypOnlineCustomSubs);
        private bool RemoteAllowed(Models.AppSettings s) => RemoteMedia && ScrolllerPolicy.Enabled
            && s.MediaSource != "local" && s.HasRemoteMediaConsent;
        private string Selection()
        {
            var s = CoreSettings.Current;
            return RemoteAllowed(s) ? s.MediaSource + "|" + string.Join("|", RemoteChannels().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) : "";
        }
        private string RefreshRemoteSelection()
        {
            var selection = Selection();
            lock (_remoteLock)
            {
                if (_remoteSelection != selection) { _remoteQueue.Clear(); _remoteSelection = selection; }
            }
            return selection;
        }
        private void KickRemotePrefetch()
        {
            var selection = RefreshRemoteSelection();
            if (selection.Length == 0) return;
            if (Interlocked.CompareExchange(ref _remoteInFlight, 1, 0) != 0) return;
            RemoteTask = System.Threading.Tasks.Task.Run(async () =>
            {
                try { await RefillRemoteAsync(selection).ConfigureAwait(false); }
                catch (Exception ex) { Log.Debug("VideoService: remote prefetch failed: {Error}", ex.Message); }
                finally { Interlocked.Exchange(ref _remoteInFlight, 0); }
            });
        }
        internal async System.Threading.Tasks.Task RefillRemoteAsync(string selection)
        {
            if (selection.Length == 0 || RefreshRemoteSelection() != selection) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            bool fetch;
            lock (_remoteLock) fetch = _remoteQueue.Count < 4;
            var queued = DownloadRemoteQueueAsync(selection);
            try
            {
                if (!fetch) return;
                var (entries, error) = await FetchRemote(timeout.Token).ConfigureAwait(false);
                if (error != null || RefreshRemoteSelection() != selection) return;
                lock (_remoteLock)
                {
                    if (_remoteSelection != selection) return;
                    foreach (var entry in entries)
                    {
                        if (_remoteQueue.Count >= 60) break;
                        if (RemoteMediaFormats.Validate(entry, FeedMediaKind.Video, out _) && !_remoteQueue.Contains(entry.Url))
                            _remoteQueue.Add(entry.Url);
                    }
                }
            }
            finally { await queued.ConfigureAwait(false); }
            await DownloadRemoteQueueAsync(selection).ConfigureAwait(false);
        }
        private async System.Threading.Tasks.Task DownloadRemoteQueueAsync(string selection)
        {
            // Only Scrolller URLs are downloaded; an uncached one is never streamed (WPF VideoService.cs:8197).
            for (var i = 0; i < 60 && RefreshRemoteSelection() == selection; i++)
            {
                string? next;
                lock (_remoteLock)
                {
                    if (_remoteQueue.Count(u => !ScrolllerPolicy.IsScrolllerUrl(u) || CachedRemote(u)) >= 3) return;
                    next = _remoteQueue.FirstOrDefault(u => ScrolllerPolicy.IsScrolllerUrl(u) && !CachedRemote(u));
                }
                if (next == null || !MayFetchRemote(next) || RefreshRemoteSelection() != selection) return;
                var path = await DownloadRemote(next, CancellationToken.None).ConfigureAwait(false);
                if (RefreshRemoteSelection() != selection) return;
                if (path == null) lock (_remoteLock) _remoteQueue.Remove(next);
            }
        }
        private string? TakeRemote()
        {
            if (RefreshRemoteSelection().Length == 0) return null;
            lock (_remoteLock)
            {
                for (int i = 0; i < _remoteQueue.Count; i++)
                {
                    var url = _remoteQueue[i];
                    if (!ScrolllerPolicy.IsScrolllerUrl(url) || CachedRemote(url))
                    { _remoteQueue.RemoveAt(i); return url; }
                    if (!MayFetchRemote(url)) _remoteQueue.RemoveAt(i--);
                }
            }
            return null;
        }
        internal System.Threading.Tasks.Task RefillRemoteForTest() => RefillRemoteAsync(RefreshRemoteSelection());

        /// <summary>WPF VideoService.MetadataCache: a clip's cached length, null on a miss. Null keeps
        /// every clip (WPF without LibVLC).</summary>
        public Func<string, double?>? DurationOf { get; set; }
        /// <summary>The last refill's enabled clips and how many the length filter kept; -1 before the first.</summary>
        public int LastFunnelEnabled { get; private set; } = -1;
        public int LastFunnelDuration { get; private set; } = -1;

        /// <summary>WPF RefillVideoQueues' duration filter: drop clips shorter than <paramref name="min"/> (0 = unset).
        /// The maximum cuts playback rather than dropping long clips from the slice pool. A clip with no cached length is kept
        /// (fall open; <paramref name="durationOf"/> starts its parse for the next refill).</summary>
        public static List<string> KeepByLength(IReadOnlyList<string> files, int min, int max, Func<string, double?>? durationOf)
        {
            if (min <= 0 || durationOf == null) return files.ToList();
            return files.Where(f => durationOf(f) is not double d || d >= min).ToList();
        }

        /// <summary>WPF RefillVideoQueues' local walk: assets/videos, recursive, supported extensions,
        /// minus the user's disabled files and folders.</summary>
        public static IReadOnlyList<string> LocalLibrary()
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "videos");
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(IsSupportedVideoExtension).ToList();
            return AssetFolderExclusion.Enabled(files, root, CoreSettings.Current);
        }

        private static void Dispose(ref ITimer? timer)
        {
            var t = timer;
            timer = null;
            t?.Dispose();
        }
    }
}
