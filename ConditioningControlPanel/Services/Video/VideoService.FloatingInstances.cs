using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.Services.Video.Browser;
using ConditioningControlPanel.Services.Video.Floating;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Floating mandatory videos as EQUAL INSTANCES (owner, 2026-10-12). One trigger opens one
    /// window with one clip; AppSettings.MandatoryVideoFloatingCount is only the most that may be
    /// up. There is no primary: every window is a browser window with its own clip and its own
    /// lifetime, kept in <see cref="_floaters"/> and nowhere else (never in <c>_windows</c>), and
    /// it closes when its own clip ends without taking another with it. No attention checks.
    ///
    /// The RUN is what the rest of the app sees as "a mandatory video is playing": it opens with
    /// the first window (<c>_videoPlaying</c>, the duck, VideoStarted, the queue slot) and ends
    /// through the ordinary <c>Cleanup</c> when the last window closes. Every stop that exists
    /// (Stop, ForceCleanup, Cleanup, CloseAll: panic, Esc, session end, engine stop) calls
    /// <see cref="CloseFloatingInstances"/>, so no window can outlive one. Which window has the
    /// sound is <see cref="FloatingAudioFocus{T}"/>.
    ///
    /// When the browser engine cannot take the clip (LibVLC route) a floating video is ONE window
    /// on the ordinary single-video path; <see cref="OnFloatingPrimaryUp"/> gives it its frame.
    /// </summary>
    public partial class VideoService
    {
        private sealed class FloatingInstance
        {
            public required BrowserVideoWindow Window { get; init; }
            public required string Clip { get; init; }
            public required Screen Screen { get; init; }
            public System.Drawing.Rectangle Bounds { get; init; }
            public IFloatingVideoSurface? Surface { get; set; }
            public DispatcherTimer? FirstFrameWatch { get; set; }
            public bool Playing { get; set; }
            // Veiled until its first frame (FloatingFirstFrame): the page's ready, playing and frame.
            public DispatcherTimer? RevealWatch { get; set; }
            public long ReadyTick { get; set; }
            public long PlayingTick { get; set; }
            public bool FrameSeen { get; set; }
            public long FirstTimeMs { get; set; } = -1;   // played time at the first report after "playing"
            public long LastTimeMs { get; set; }
            public bool Revealed { get; set; }
            public long SpawnTick { get; init; } = Environment.TickCount64;
            public bool Closed { get; set; }
            public long PositionMs { get; set; }
            public long DurationMs { get; set; }
            public bool CutStartChosen { get; set; }
        }

        /// <summary>Oldest first. UI thread only.</summary>
        private readonly List<FloatingInstance> _floaters = new();
        private readonly FloatingAudioFocus<IFloatingVideoSurface> _audioFocus = new();
        /// <summary>The instances own the current video run: the last one to close ends it.</summary>
        private bool _floatRunOpen;
        /// <summary>Windows playing their closing animation, and what closes each one now.</summary>
        private readonly Dictionary<Window, Action> _vanishing = new();
        /// <summary>The run is ending because its last window closed: that window keeps its animation.</summary>
        private bool _spareVanishing;

        // LibVLC route only: the single window's frame, silenced by deselecting ITS audio track
        // (LibVLC Mute/Volume are process-wide). With one window it never is.
        private IFloatingVideoSurface? _primarySurface;
        private bool _primaryFocusMuted;
        private bool _primaryEverFocusMuted;
        private int _primaryAudioTrack = -1;

        /// <summary>An instance is another window's WebView2: the duck sweep must not lower it.</summary>
        internal bool HasFloatingInstances => _floaters.Count > 0;

        private const int InstanceFirstFrameMs = 25000;

        private static int FloatingLimit()
            => MandatoryVideoPresentation.ClampCount(App.Settings?.Current?.MandatoryVideoFloatingCount ?? 1);

        private static double FloatingMotionSpeed()
            => MandatoryVideoPresentation.ClampSpeedPercent(
                App.Settings?.Current?.MandatoryVideoFloatingSpeedPercent ?? MandatoryVideoPresentation.DefaultSpeedPercent) / 100.0;

        private static string ClipIdentity(string path) => BrowserUnsafeVideoCache.ResolveStableKey(path) ?? path;

        /// <summary>The floating setting applies to a video asked for right now.</summary>
        private bool FloatingWanted(bool strict)
        {
            try
            {
                return MandatoryVideoPresentation.ShouldFloat(
                    App.Settings?.Current?.MandatoryVideoFloating == true,
                    strict || IsStrictActive,
                    App.Lockdown?.IsActive == true,
                    App.Chaos?.IsRunning == true);
            }
            catch (Exception ex) { Diag.Swallowed(ex); return false; }
        }

        // ===================== triggers =====================

        /// <summary>
        /// A trigger while floating windows are up. True = handled here (one more window, or a
        /// quiet skip at the limit) and the caller returns: it must not force the run down the way
        /// a full-screen trigger replaces a video. False = not a floating case, the ordinary path
        /// runs (a strict video, a Lockdown or a Chaos run still takes the screen and closes these).
        /// </summary>
        private bool TryFloatingTrigger(bool? strictOverride, bool userInitiated)
        {
            if (!_floatRunOpen || _floaters.Count == 0) return false;
            if (!FloatingWanted(strictOverride ?? App.Settings?.Current?.StrictLockEnabled == true)) return false;
            DispatcherHelper.RunOnUI(() => AddFloatingInstance(replaceOldestAtLimit: userInitiated));
            return true;
        }

        /// <summary>One more window in the open run. At the limit an automatic trigger is skipped
        /// quietly; a Test press closes the oldest first.</summary>
        private bool AddFloatingInstance(bool replaceOldestAtLimit)
        {
            try
            {
                if (!_floatRunOpen || !_videoPlaying || _isCleaningUp || _gracePaused) return false;
                if (!BrowserVideoEngine.Instance.IsAvailable) return false;
                var decision = MandatoryVideoPresentation.DecideTrigger(_floaters.Count, FloatingLimit(), replaceOldestAtLimit);
                if (decision == MandatoryVideoPresentation.TriggerDecision.Skip)
                {
                    VideoDiag.Log("VIDEO", $"floating trigger skipped - {_floaters.Count} up, that is the limit");
                    return false;
                }
                var clip = PickFloatingClip();
                if (clip == null) return false;
                if (decision == MandatoryVideoPresentation.TriggerDecision.ReplaceOldest)
                    while (_floaters.Count >= FloatingLimit())
                        CloseInstance(_floaters[0], "the oldest makes room", endRun: false);
                var ok = SpawnInstance(clip);
                if (_floaters.Count == 0) EndFloatingRun("nothing could replace the last window");
                return ok;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: floating add failed - {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The first window of a run, from PlayVideo: the clip is chosen and nothing is playing.
        /// True = the run is open on a floating instance and PlayVideo returns. False = this clip
        /// goes down the ordinary single-window path (full screen, or the LibVLC route).
        /// </summary>
        private bool TryStartFloatingRun(string path, bool strict)
        {
            try
            {
                if (!FloatingWanted(strict)) return false;
                if (!BrowserVideoEngine.Instance.IsAvailable || !BrowserVideoGate.ShouldUseBrowser(path)) return false;
                if (BrowserVideoEngine.BuildPageUrl(path) == null) return false;
            }
            catch (Exception ex) { Diag.Swallowed(ex); return false; }

            // What PlayVideo does for any video, minus the guards that watch ONE player.
            ++_videoRunId;
            _safetyTimer?.Stop();
            _fallbackSafetyTimer?.Stop();
            _fallbackSafetyTimer = null;
            _maxLenCapTimer?.Stop();
            _maxLenCapTimer = null;
            _videoPlaying = true;
            _playbackStarted = true;
            _strictActive = false;
            CancelPendingRetry();
            _startTime = DateTime.Now;
            ClearGraceState();
            _gracePauseConsumed = false;
            _duration = 0;
            _hits = _total = 0;
            _spawnTimes.Clear();
            ClearFloating();
            _floatRunOpen = true;

            try { App.DiscordRpc?.SetVideoActivity(); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { VideoAboutToStart?.Invoke(this, EventArgs.Empty); } catch (Exception ex) { Diag.Swallowed(ex); }
            // No Flash.Stop: a floating video leaves the flashes up (VideoFlashRule).
            if (App.Settings?.Current?.AudioDuckingEnabled == true)
            {
                App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                _didDuck = true;
            }

            if (!SpawnInstance(path))
            {
                EndFloatingRun("the first window could not start");
                return true;
            }

            try { App.Bubbles?.PauseAndClear(); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { VideoStarted?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("VideoStarted handler error: {Error}", ex.Message); }
            // The scheduler is normally re-armed only when a video ends. A floating run leaves
            // room for the next trigger, so it keeps ticking while windows are up, on the shorter
            // join delay: a full gap was reset by the run's own Cleanup before it ever fired, so
            // two windows almost never met.
            if (_isRunning) ScheduleNext(FloatingJoinDelay());
            return true;
        }

        /// <summary>When the next window may join the open run (MandatoryVideoPresentation.JoinDelaySeconds).
        /// Null = no run open, or a limit of one: the ordinary full gap.</summary>
        private double? FloatingJoinDelay()
        {
            if (!_floatRunOpen || _floaters.Count == 0) return null;
            return MandatoryVideoPresentation.JoinDelaySeconds(
                App.Settings?.Current?.VideosPerHour ?? 1, FloatingLimit(), _random.NextDouble());
        }

        /// <summary>The scheduler may fire during a floating run: its trigger lands in
        /// <see cref="TryFloatingTrigger"/> (one more window, or a quiet skip at the limit).</summary>
        private bool SchedulerMayTrigger => !_videoPlaying || (_floatRunOpen && _floaters.Count > 0);

        /// <summary>The last window closed on its own: the run ends the way any video ends.</summary>
        private void EndFloatingRun(string reason)
        {
            if (!_floatRunOpen) return;
            _floatRunOpen = false;
            VideoDiag.Log("VIDEO", $"floating run ends ({reason})");
            _spareVanishing = true;
            try { if (_videoPlaying && !_isCleaningUp) Cleanup(); }
            finally { _spareVanishing = false; }
        }

        // ===================== frames and the one sound =====================

        /// <summary>LibVLC route: the single floating window is on screen, give it its frame.</summary>
        private void OnFloatingPrimaryUp(string path)
        {
            try
            {
                if (!IsFloating || !_videoPlaying || _isCleaningUp) return;
                var win = _primaryVideoWindow;
                if (win == null || !win.IsVisible) return;
                var old = _primarySurface;
                if (old != null) DropSurface(old);
                _primaryFocusMuted = false;
                _primaryEverFocusMuted = false;
                _primaryAudioTrack = -1;
                _primarySurface = FloatingVideoFrame.Attach(win, SetPrimaryFocusMuted);
                AdoptSurface(_primarySurface, _floatScreen!);
                ApplyAudioFocus();
            }
            catch (Exception ex) { App.Logger?.Warning("VideoService: floating frame setup failed - {Error}", ex.Message); }
        }

        /// <param name="joinAudio">False for a window still veiled (FloatingFirstFrame): it takes no sound
        /// until it is shown, RevealInstance adds it then.</param>
        private void AdoptSurface(IFloatingVideoSurface surface, Screen screen, bool joinAudio = true)
        {
            surface.Touched += OnFloatingTouched;
            if (joinAudio) _audioFocus.Add(surface);
            try
            {
                surface.MotionSpeed = FloatingMotionSpeed();
                var wa = screen.WorkingArea;
                surface.StartMotion(new Rect(wa.X, wa.Y, wa.Width, wa.Height));
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void DropSurface(IFloatingVideoSurface surface)
        {
            surface.Touched -= OnFloatingTouched;
            _audioFocus.Remove(surface);
            try { surface.Detach(); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void OnFloatingTouched(IFloatingVideoSurface surface)
        {
            if (_audioFocus.Touch(surface)) ApplyAudioFocus();
        }

        /// <summary>Exactly one window unmuted and lit: the holder.</summary>
        private void ApplyAudioFocus()
        {
            foreach (var s in _audioFocus.Items.ToList())
            {
                var has = _audioFocus.HasSound(s);
                try
                {
                    s.SetMuted(!has);
                    s.AudioLit = has;
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
        }

        /// <summary>The speed slider moved: every open window takes the new speed at once.</summary>
        public void RefreshFloatingMotionSpeed()
        {
            try
            {
                var speed = FloatingMotionSpeed();
                foreach (var s in _audioFocus.Items.ToList())
                {
                    try { s.MotionSpeed = speed; }
                    catch (Exception ex) { Diag.Swallowed(ex); }
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void SetPrimaryFocusMuted(bool muted)
        {
            if (!muted && !_primaryEverFocusMuted) return;
            _primaryFocusMuted = muted;
            _primaryEverFocusMuted = true;
            if (_browserActive) return;   // the frame silences a browser window's page itself
            var player = _primaryMediaPlayer;
            if (player != null) Task.Run(() => ApplyPrimaryAudioTrack(player));
        }

        /// <summary>Silences or restores the LibVLC window through its OWN audio track. Off the UI
        /// thread, and inside the player lock with the ownership check the retry path uses, so it
        /// can never land on a player a teardown has taken.</summary>
        private void ApplyPrimaryAudioTrack(LibVLCSharp.Shared.MediaPlayer player)
        {
            try
            {
                lock (_mediaPlayersLock)
                {
                    if (_isCleaningUp || !_videoPlaying || !_mediaPlayers.Contains(player)) return;
                    var current = player.AudioTrack;
                    if (_primaryFocusMuted)
                    {
                        if (current == -1) return;
                        _primaryAudioTrack = current;
                        player.SetAudioTrack(-1);
                    }
                    else if (current == -1)
                    {
                        var id = _primaryAudioTrack;
                        if (id == -1)
                            id = player.AudioTrackDescription.Select(d => d.Id).FirstOrDefault(i => i != -1, -1);
                        if (id != -1) player.SetAudioTrack(id);
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: floating audio track switch failed - {Error}", ex.Message); }
        }

        /// <summary>A volume change reaches every instance: the real volume, never a focus mute
        /// (that is the frame's, through SetMuted).</summary>
        private void RefreshFloatingVolumes()
        {
            var eff = GetEffectiveVolume();
            foreach (var c in _floaters.ToList())
            {
                try { c.Window.Post(new { type = "setVolume", volume = eff / 100.0, muted = eff <= 0 }); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
        }

        /// <summary>The grace pause holds every window, and its release starts them again.</summary>
        private void SetFloatingPaused(bool paused)
        {
            foreach (var c in _floaters.ToList())
            {
                try { c.Window.Post(new { type = paused ? "pause" : "resume" }); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            // The pause shortened the queue's window to the pause itself; give the clips theirs back.
            if (!paused) RenewFloatingQueueTimeout();
        }

        /// <summary>The queue's stuck recovery must not take a healthy run: its window (counted
        /// from now) covers the longest clip still playing. It stays the net under a window that
        /// stalls, since nothing else watches an instance after its first frame.</summary>
        private void RenewFloatingQueueTimeout()
        {
            try
            {
                if (_floaters.Count == 0) return;
                var longest = _floaters.Max(f => Math.Max(0, f.DurationMs - f.PositionMs)) / 1000.0;
                if (longest > 0)
                    App.InteractionQueue?.ExtendTimeout(longest + 90, InteractionQueueService.InteractionType.Video);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>The screens the open windows started on: where the grace card goes.</summary>
        private List<Screen> FloatingScreens()
            => _floaters.Select(f => f.Screen).GroupBy(s => s.DeviceName).Select(g => g.First()).ToList();

        // ===================== instances =====================

        /// <summary>A clip for one more window: not one already up while the pool allows, and one
        /// the page can play.</summary>
        private string? PickFloatingClip()
        {
            var up = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _floaters) up.Add(ClipIdentity(c.Clip));

            string? repeat = null;
            for (var i = 0; i < 6; i++)
            {
                var p = GetNextVideo();
                if (p == null) break;
                if (!BrowserVideoGate.ShouldUseBrowser(p)) continue;
                if (!up.Contains(ClipIdentity(p))) return p;
                repeat ??= p;
            }
            return repeat;
        }

        private System.Drawing.Rectangle InstanceRectNow(FloatingInstance f)
        {
            try
            {
                var hwnd = new WindowInteropHelper(f.Window).Handle;
                if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) && r.Right - r.Left > 0)
                    return new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            return f.Bounds;
        }

        private bool SpawnInstance(string clip)
        {
            BrowserVideoWindow? win = null;
            try
            {
                var url = BrowserVideoEngine.BuildPageUrl(clip);
                if (url == null) return false;
                var screens = VideoScreens();
                if (screens == null || screens.Length == 0) return false;
                var screen = screens[_random.Next(screens.Length)];

                // Overlap is allowed; only an exact pile on a window already there is avoided.
                var rect = FloatRectOn(screen, _floaters.Select(InstanceRectNow).ToList());

                win = new BrowserVideoWindow(screen, primary: false, rect);
                // "via Scrolller" badge + end card. Floating runs are never strict; Lockdown and
                // Chaos still make the badge not pressable.
                var credit = Video.ScrolllerCredit.PagePayload(clip, Video.ScrolllerCredit.CanOpenLink(
                    false, App.Lockdown?.IsActive == true, App.Chaos?.IsRunning == true));
                var c = new FloatingInstance { Window = win, Clip = clip, Screen = screen, Bounds = rect };
                win.Message += OnInstanceMessage;
                win.InitFailed += OnInstanceInitFailed;
                win.ProcessFailed += OnInstanceProcessFailed;
                win.Ready += OnInstanceReady;

                // Loaded with the real volume, unmuted: the focus pass below silences every window
                // but the newest through its frame, and a window loaded muted could never take the sound.
                var eff = GetEffectiveVolume();
                win.Post(new
                {
                    type = "load",
                    url,
                    volume = eff / 100.0,
                    muted = eff <= 0,
                    blurBackground = App.Settings?.Current?.VideoBlurredBackgroundEnabled == true,
                    hideCursor = false,
                    startAtMs = 0L,
                    // Gentle visuals: the slow speed and the fade from black (1 and 0 when off).
                    rate = 1.0,   // videos play at normal speed under gentle visuals (owner, 2026-10-14)
                    fadeMs = Safety.GentleVisuals.On ? Safety.GentleVisuals.FadeMs(0) : 0,
                    strict = false,
                    sinkLabel = BrowserSinkLabel.Resolve(),
                    credit,
                });
                // The frame goes on BEFORE the show, veiled: the window must be shown for its WebView2 to
                // start, and must not be seen until its first frame (FloatingFirstFrame).
                c.Surface = FloatingVideoFrame.Attach(win);   // a browser window: the frame silences it
                c.Surface.Veil();
                win.Show();              // never activated: a floating browser window takes no focus
                PreventClickRaise(win);

                _floaters.Add(c);
                // Motion now, sound at the reveal: a window nobody can see never plays, and never takes
                // the sound from the clip being watched.
                AdoptSurface(c.Surface, screen, joinAudio: false);
                c.Surface.SetMuted(true);

                // No picture in time: this window goes, the others carry on.
                var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(InstanceFirstFrameMs) };
                watch.Tick += (_, _) => { watch.Stop(); if (!c.Revealed) CloseInstance(c, "no first frame"); };
                c.FirstFrameWatch = watch;
                watch.Start();
                StartRevealWatch(c);

                _ = InitInstanceAsync(c);
                VideoDiag.Log("VIDEO", $"floating window {_floaters.Count} of {FloatingLimit()} up at {rect} - {System.IO.Path.GetFileName(clip)}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("VideoService: floating window could not start - {Error}", ex.Message);
                var half = _floaters.FirstOrDefault(x => ReferenceEquals(x.Window, win));
                if (half != null) CloseInstance(half, "start failed", endRun: false);
                else try { win?.Close(); } catch { }
                return false;
            }
        }

        // ===================== first frame, then the window (FloatingFirstFrame) =====================

        private void OnInstanceReady(BrowserVideoWindow win)
        {
            var c = _floaters.FirstOrDefault(x => ReferenceEquals(x.Window, win));
            if (c != null && c.ReadyTick == 0) c.ReadyTick = Environment.TickCount64;
        }

        private void StartRevealWatch(FloatingInstance c)
        {
            var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            watch.Tick += (_, _) => CheckReveal(c);
            c.RevealWatch = watch;
            watch.Start();
        }

        /// <summary>The window stays veiled until its first frame; a clip that cannot get there in
        /// time is skipped and its window closed, never shown black.</summary>
        private void CheckReveal(FloatingInstance c)
        {
            if (c.Closed || c.Revealed) { c.RevealWatch?.Stop(); return; }
            var now = Environment.TickCount64;
            // A grace pause holds every page: its time does not count against the first frame.
            if (_gracePaused)
            {
                if (c.ReadyTick != 0) c.ReadyTick += 100;
                if (c.PlayingTick != 0) c.PlayingTick += 100;
                return;
            }
            switch (FloatingFirstFrame.Decide(c.FrameSeen,
                        c.PlayingTick == 0 ? null : now - c.PlayingTick,
                        c.FirstTimeMs < 0 ? 0 : c.LastTimeMs - c.FirstTimeMs,
                        c.ReadyTick == 0 ? null : now - c.ReadyTick))
            {
                case FloatingFirstFrame.Verdict.Reveal:
                    RevealInstance(c);
                    break;
                case FloatingFirstFrame.Verdict.Skip:
                    c.RevealWatch?.Stop();
                    VideoDiag.Log("VIDEO", $"floating window skipped - no first frame {FloatingFirstFrame.ReadyBudgetMs}ms after the page was ready (playing={c.Playing}) - {System.IO.Path.GetFileName(c.Clip)}");
                    CloseInstance(c, "no first frame in time");
                    break;
            }
        }

        private void RevealInstance(FloatingInstance c)
        {
            if (c.Closed || c.Revealed) return;
            c.Revealed = true;
            c.RevealWatch?.Stop();
            if (c.Surface != null)
            {
                // Shown: it joins the sound rotation as the newest, so it takes the sound (as a
                // window did on creation before the veil).
                _audioFocus.Add(c.Surface);
                ApplyAudioFocus();
            }
            var animate = FloatingSpawnLook.Animates(MotionFx.Level);
            VideoDiag.Log("VIDEO", $"floating window shown {Environment.TickCount64 - c.SpawnTick}ms after it was made (frame={c.FrameSeen}, animated={animate})");
            void Revealed()
            {
                // This window's mute and close (VideoService.Controls.cs), once it is fully shown and,
                // under Gentle visuals, after the page's own fade.
                if (!c.Closed) ShowInstanceControls(c.Window, Safety.GentleVisuals.On ? Safety.GentleVisuals.FadeMs(0) : 0);
            }
            if (c.Surface != null) c.Surface.Reveal(animate, Revealed);
            else Revealed();
        }

        /// <summary>The mute and close buttons of one window go with it.</summary>
        private void CloseControlsOf(Window host)
        {
            foreach (var x in _videoControls.Where(x => ReferenceEquals(x.Host, host)).ToList())
            {
                try { x.Close(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
        }

        private async Task InitInstanceAsync(FloatingInstance c)
        {
            try
            {
                var env = await BrowserVideoEngine.SharedEnvironmentAsync().ConfigureAwait(true);
                if (c.Closed) return;
                if (env == null) { CloseInstance(c, "no WebView2 environment"); return; }
                await c.Window.InitAsync(env, BrowserVideoEngine.SharedMappings(),
                    BrowserVideoEngine.PlayerStartUrl, BrowserVideoEngine.PlayerHost).ConfigureAwait(true);
                if (!_videoPlaying) CloseFloatingInstances("the run ended while a window was starting");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: floating window init failed - {Error}", ex.Message);
                CloseInstance(c, "init threw");
            }
        }

        private void OnInstanceMessage(BrowserVideoWindow win, JObject o)
        {
            var c = _floaters.FirstOrDefault(x => ReferenceEquals(x.Window, win));
            if (c == null || c.Closed) return;
            // Never acted on inside the WebView2 message callback (same rule as the session).
            switch ((string?)o["type"])
            {
                case "playing":
                    c.Playing = true;
                    if (c.PlayingTick == 0) c.PlayingTick = Environment.TickCount64;
                    // The mute and close buttons come with the reveal (RevealInstance).
                    break;
                case "firstFrame":
                    c.FrameSeen = true;
                    VideoDiag.Log("VIDEO", $"floating page painted: via={(string?)o["via"]} frames={(int?)o["frames"]} mediaMs={(long?)o["mediaMs"]} visibility={(string?)o["visibility"]}");
                    PostAfterPageMessage(() => CheckReveal(c));
                    break;
                case "meta":
                    c.DurationMs = (long?)o["durationMs"] ?? 0;
                    if (!c.CutStartChosen && c.DurationMs > 0)
                    {
                        c.CutStartChosen = true;
                        var offset = App.Settings?.Current?.VideoCutRandomStart == true
                            ? VideoLengthCap.StartOffsetMs(App.Settings.Current.VideoMaxDurationSeconds, c.DurationMs, _random) : 0;
                        if (offset > 0) PostAfterPageMessage(() => { if (!c.Closed) c.Window.Post(new { type = "seek", ms = offset }); });
                    }
                    RenewFloatingQueueTimeout();
                    break;
                case "time":
                    c.PositionMs = Math.Max(c.PositionMs, (long?)o["playedMs"] ?? 0);
                    if (c.Playing && (bool?)o["paused"] != true)
                    {
                        var at = (long?)o["playedMs"] ?? 0;   // played time: a seek is not progress
                        if (c.FirstTimeMs < 0) c.FirstTimeMs = at;
                        c.LastTimeMs = at;
                    }
                    // The player's longest-video limit, per window: the run's own cap timer is
                    // off here (it watches one player), so each clip is cut on what it has played.
                    if (VideoLengthCap.Reached(App.Settings?.Current?.VideoMaxDurationSeconds ?? 0,
                            c.PositionMs))
                    {
                        PostAfterPageMessage(() => CloseInstance(c, "longest-video limit reached", animate: true));
                        break;
                    }
                    // No run left to belong to: the window goes.
                    if (!_videoPlaying)
                        PostAfterPageMessage(() => { if (!_videoPlaying) CloseFloatingInstances("no run left"); });
                    break;
                case "ended":
                    PostAfterPageMessage(() => CloseInstance(c, "clip ended", watchedToEnd: true, animate: true));
                    break;
                case "error":
                    PostAfterPageMessage(() => CloseInstance(c, "page error " + ((int?)o["code"] ?? 0)));
                    break;
                case "click":
                    // The page posts this on pointerdown: a touch (the frame reports its own too).
                    PostAfterPageMessage(() => { if (!c.Closed && c.Surface != null) OnFloatingTouched(c.Surface); });
                    break;
                case "creditClick":
                    // A floating window is not topmost over everything, so the link opens right away.
                    PostAfterPageMessage(() =>
                    {
                        if (c.Closed || !Video.ScrolllerCredit.CanOpenLink(false, App.Lockdown?.IsActive == true, App.Chaos?.IsRunning == true)) return;
                        Video.ScrolllerCredit.Open(c.Clip);
                    });
                    break;
            }
        }

        private void OnInstanceInitFailed(BrowserVideoWindow win, string reason)
            => PostAfterPageMessage(() => CloseInstanceOf(win, "WebView2 init failed: " + reason));

        private void OnInstanceProcessFailed(BrowserVideoWindow win, CoreWebView2ProcessFailedKind kind)
        {
            try { BrowserVideoEngine.ReportProcessFailure(kind); } catch (Exception ex) { Diag.Swallowed(ex); }
            PostAfterPageMessage(() => CloseInstanceOf(win, "browser process failed"));
        }

        private void CloseInstanceOf(BrowserVideoWindow win, string reason)
        {
            var c = _floaters.FirstOrDefault(x => ReferenceEquals(x.Window, win));
            if (c != null) CloseInstance(c, reason);
        }

        /// <summary>A clip counts the way a full-screen one does: watch time for what played, and
        /// "watched to the end" (Circe's tab) only for a clip that reached its end.</summary>
        private void CreditInstance(FloatingInstance c, bool watchedToEnd)
        {
            try
            {
                var watchedSec = c.PositionMs / 1000.0;
                var durationSec = c.DurationMs / 1000.0;
                if (watchedSec >= 1.0)
                {
                    App.Achievements?.TrackVideoWatched(watchedSec);
                    VideoWatchCredited?.Invoke(this, new VideoWatchInfoEventArgs
                    {
                        WatchedSec = watchedSec,
                        DurationSec = durationSec,
                        EndedNaturally = watchedToEnd || (durationSec > 0 && watchedSec >= durationSec * 0.90),
                    });
                }
                if (watchedToEnd) App.Chaster?.Note("video");
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: floating watch credit failed - {Error}", ex.Message); }
        }

        /// <summary>Close ONE window. Idempotent. It takes nothing with it: when it had the sound
        /// the most recent survivor takes it, and only the last one to go ends the run.</summary>
        /// <param name="animate">A clip that ended or reached the limit closes with the short
        /// closing animation (FloatingSpawnLook); every stop, skip and failure closes at once.</param>
        private void CloseInstance(FloatingInstance c, string reason, bool watchedToEnd = false, bool endRun = true, bool refocus = true, bool animate = false)
        {
            if (c.Closed) return;
            c.Closed = true;
            _floaters.Remove(c);
            c.FirstFrameWatch?.Stop();
            c.RevealWatch?.Stop();
            CreditInstance(c, watchedToEnd);
            var w = c.Window;
            var surface = c.Surface;
            if (surface != null)
            {
                // Out of the sound rotation now; the frame itself lets go when the window does.
                surface.Touched -= OnFloatingTouched;
                _audioFocus.Remove(surface);
            }
            try
            {
                w.Message -= OnInstanceMessage;
                w.InitFailed -= OnInstanceInitFailed;
                w.ProcessFailed -= OnInstanceProcessFailed;
                w.Ready -= OnInstanceReady;
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            CloseControlsOf(w);   // the buttons go first, never left over a closing window

            var finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                _vanishing.Remove(w);
                try { surface?.Detach(); } catch (Exception ex) { Diag.Swallowed(ex); }
                try
                {
                    if (w.IsReady) w.Post(new { type = "stop" });
                    w.Close();
                }
                catch (Exception ex) { App.Logger?.Debug("VideoService: floating window close failed - {Error}", ex.Message); }
            }

            if (animate && surface != null && c.Revealed && FloatingSpawnLook.Animates(MotionFx.Level))
            {
                try { surface.SetMuted(true); } catch (Exception ex) { Diag.Swallowed(ex); }   // the frame adds silence
                _vanishing[w] = Finish;
                surface.Vanish(true, Finish);
            }
            else Finish();
            VideoDiag.Log("VIDEO", $"floating window closed ({reason}), {_floaters.Count} left");
            if (refocus) ApplyAudioFocus();
            if (endRun && _floaters.Count == 0) EndFloatingRun("last window closed: " + reason);
        }

        /// <summary>EVERY window goes, now, and the run is no longer theirs to end (the stop that
        /// called this is ending it). Called first thing by every teardown (CloseAll, and by hand
        /// from Stop / Cleanup / ForceCleanup in case CloseAll declines as a duplicate).
        /// Idempotent, never throws.</summary>
        private void CloseFloatingInstances(string reason)
        {
            _floatRunOpen = false;
            if (_floaters.Count == 0 && _vanishing.Count == 0) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                try { dispatcher.BeginInvoke(new Action(() => CloseFloatingInstances(reason))); }
                catch (Exception ex) { Diag.Swallowed(ex); }
                return;
            }
            // A window still playing its closing animation goes now too (panic, a stop), unless
            // this is the run ending because that very window was the last one.
            if (!_spareVanishing)
                foreach (var finish in _vanishing.Values.ToList())
                {
                    try { finish(); } catch (Exception ex) { Diag.Swallowed(ex); }
                }
            foreach (var c in _floaters.ToList())
            {
                try { CloseInstance(c, reason, endRun: false, refocus: false); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            try { ApplyAudioFocus(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>The whole floating layer of a video that is being torn down: every instance,
        /// the LibVLC window's frame, the sound bookkeeping.</summary>
        private void CloseFloatingLayer(string reason)
        {
            try
            {
                CloseFloatingInstances(reason);
                var primary = _primarySurface;
                _primarySurface = null;
                if (primary != null) DropSurface(primary);
                if (_floaters.Count == 0) _audioFocus.Clear();
                _primaryFocusMuted = false;
                _primaryEverFocusMuted = false;
                _primaryAudioTrack = -1;
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: floating layer close failed - {Error}", ex.Message); }
        }

        // ===================== Test button =====================

        /// <summary>
        /// Test pressed while a video plays. In floating mode there is never a "video appears to
        /// be playing" dialog: the press adds one window, and at the limit the oldest makes room
        /// (on the LibVLC route, where one window is all there is, the press replaces it). False
        /// when the video on screen is not a floating one, and the caller keeps its dialog.
        /// </summary>
        public bool TryAddFloatingTestVideo()
        {
            try
            {
                if (!_videoPlaying || _strictActive) return false;
                if (!FloatingWanted(App.Settings?.Current?.StrictLockEnabled == true)) return false;
                if (_floatRunOpen && _floaters.Count > 0)
                {
                    AddFloatingInstance(replaceOldestAtLimit: true);
                    return true;
                }
                if (!IsFloating) return false;
                // One LibVLC window: it is the oldest, so it makes room. TriggerVideo replaces a
                // video that is up, which is all a Test press ever did after "Yes".
                TriggerVideo(userInitiated: true);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: floating test add failed - {Error}", ex.Message);
                return false;
            }
        }
    }
}
