using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.Services.Video.Browser;
using ConditioningControlPanel.Services.Video.Floating;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// More than one floating video (AppSettings.MandatoryVideoFloatingCount) and the one sound.
    /// The FIRST window is the video: it stays on the path a single video runs (lifetime, attention
    /// checks, grace card, cleanup) and knows nothing about the rest. The others are COMPANIONS: a
    /// browser window each, its own clip, no targets, kept in <see cref="_companions"/> and nowhere
    /// else. They are never in <c>_windows</c>, never set <c>_videoPlaying</c>, never duck and never
    /// hold other effects, so a companion cannot keep a video "playing". Every teardown reaches
    /// <c>CloseAll</c>, whose first line closes them all; a companion also closes on its own when
    /// its clip ends. Which window has the sound is <see cref="FloatingAudioFocus{T}"/>.
    /// </summary>
    public partial class VideoService
    {
        private sealed class FloatingCompanion
        {
            public required BrowserVideoWindow Window { get; init; }
            public required string Clip { get; init; }
            public System.Drawing.Rectangle Bounds { get; init; }
            public IFloatingVideoSurface? Surface { get; set; }
            public DispatcherTimer? FirstFrameWatch { get; set; }
            public bool Playing { get; set; }
            public bool Closed { get; set; }
        }

        /// <summary>Oldest first. UI thread only.</summary>
        private readonly List<FloatingCompanion> _companions = new();
        private readonly FloatingAudioFocus<IFloatingVideoSurface> _audioFocus = new();
        private IFloatingVideoSurface? _primarySurface;
        private string? _floatPrimaryClip;

        // The engines always carry the REAL volume, unmuted: the frame can only add silence, and
        // lifting it restores what the engine last loaded. A browser window is silenced by the
        // frame itself; the LibVLC primary through this delegate state, by deselecting ITS audio
        // track (LibVLC Mute/Volume are process-wide).
        private bool _primaryFocusMuted;
        private bool _primaryEverFocusMuted;
        private int _primaryAudioTrack = -1;

        /// <summary>A companion is another window's WebView2: the duck sweep must not lower it.</summary>
        private bool HasFloatingCompanions => _companions.Count > 0;

        private const int CompanionFirstFrameMs = 25000;

        private static int FloatingWindowCount()
            => MandatoryVideoPresentation.ClampCount(App.Settings?.Current?.MandatoryVideoFloatingCount ?? 1);

        private static string ClipIdentity(string path) => BrowserUnsafeVideoCache.ResolveStableKey(path) ?? path;

        // ===================== the primary's frame, and the event's companions =====================

        /// <summary>The primary window of a floating video is on screen (either engine, and again
        /// after a browser to LibVLC handoff): give it its frame, then open the companions.</summary>
        private void OnFloatingPrimaryUp(string path)
        {
            try
            {
                if (!IsFloating || !_videoPlaying || _isCleaningUp) return;
                var win = _primaryVideoWindow;
                if (win == null || !win.IsVisible) return;
                _floatPrimaryClip = ClipIdentity(path);

                var holder = _audioFocus.Holder;
                var old = _primarySurface;
                if (old != null)
                {
                    if (ReferenceEquals(holder, old)) holder = null;
                    DropSurface(old);
                }
                _primaryFocusMuted = false;
                _primaryEverFocusMuted = false;
                _primaryAudioTrack = -1;
                _primarySurface = FloatingVideoFrame.Attach(win, SetPrimaryFocusMuted);
                AdoptSurface(_primarySurface);
                if (holder != null) _audioFocus.Touch(holder);   // a handoff does not move the sound
                ApplyAudioFocus();

                var want = FloatingWindowCount() - 1;
                while (_companions.Count < want && SpawnCompanion()) { }
            }
            catch (Exception ex) { App.Logger?.Warning("VideoService: floating frame setup failed - {Error}", ex.Message); }
        }

        private void AdoptSurface(IFloatingVideoSurface surface)
        {
            surface.Touched += OnFloatingTouched;
            _audioFocus.Add(surface);
            try
            {
                var wa = _floatScreen!.WorkingArea;
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

        // ===================== the one sound, per engine =====================

        private void SetPrimaryFocusMuted(bool muted)
        {
            // A single floating video never comes through here with a change: no extra call on the
            // path the owner tested.
            if (!muted && !_primaryEverFocusMuted) return;
            _primaryFocusMuted = muted;
            _primaryEverFocusMuted = true;
            if (_browserActive) return;   // the frame silences a browser window's page itself
            var player = _primaryMediaPlayer;
            if (player != null) Task.Run(() => ApplyPrimaryAudioTrack(player));
        }

        /// <summary>Silences or restores the LibVLC primary through its OWN audio track. Off the UI
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

        /// <summary>A volume change reaches every companion: the real volume, never a focus mute
        /// (that is the frame's, through SetMuted).</summary>
        private void RefreshCompanionVolumes()
        {
            var eff = GetEffectiveVolume();
            foreach (var c in _companions.ToList())
            {
                try { c.Window.Post(new { type = "setVolume", volume = eff / 100.0, muted = eff <= 0 }); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
        }

        /// <summary>The grace pause holds every window, and its release starts them again.</summary>
        private void SetCompanionsPaused(bool paused)
        {
            foreach (var c in _companions.ToList())
            {
                try { c.Window.Post(new { type = paused ? "pause" : "resume" }); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
        }

        // ===================== companions =====================

        /// <summary>A clip for one more window: not one already up while the pool allows, and one
        /// the page can play (a companion is a browser window).</summary>
        private string? PickCompanionClip()
        {
            var up = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_floatPrimaryClip != null) up.Add(_floatPrimaryClip);
            foreach (var c in _companions) up.Add(ClipIdentity(c.Clip));

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

        private bool SpawnCompanion()
        {
            if (!IsFloating || !_videoPlaying || _isCleaningUp || _gracePaused) return false;
            if (_companions.Count >= MandatoryVideoPresentation.MaxWindows - 1) return false;

            BrowserVideoWindow? win = null;
            try
            {
                var clip = PickCompanionClip();
                var url = clip == null ? null : BrowserVideoEngine.BuildPageUrl(clip);
                if (clip == null || url == null) return false;

                var screen = _floatScreen!;
                CaptureFloatBounds();   // the primary may have been moved
                var taken = new List<System.Drawing.Rectangle> { _floatBounds!.Value };
                taken.AddRange(_companions.Select(c => c.Bounds));
                var rect = FloatRectOn(screen, MandatoryVideoPresentation.CompanionMinWidth, taken);

                win = new BrowserVideoWindow(screen, primary: false, rect);
                var c = new FloatingCompanion { Window = win, Clip = clip, Bounds = rect };
                win.Message += OnCompanionMessage;
                win.InitFailed += OnCompanionInitFailed;
                win.ProcessFailed += OnCompanionProcessFailed;

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
                    strict = false,
                    sinkLabel = BrowserSinkLabel.Resolve(),
                });
                win.Show();              // never activated: a floating browser window takes no focus
                PreventClickRaise(win);

                _companions.Add(c);
                c.Surface = FloatingVideoFrame.Attach(win);   // a browser window: the frame silences it
                AdoptSurface(c.Surface);
                ApplyAudioFocus();

                // No picture in time: the window goes, the video carries on.
                var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CompanionFirstFrameMs) };
                watch.Tick += (_, _) => { watch.Stop(); if (!c.Playing) CloseCompanion(c, "no first frame"); };
                c.FirstFrameWatch = watch;
                watch.Start();

                _ = InitCompanionAsync(c);
                VideoDiag.Log("VIDEO", $"floating companion {_companions.Count} up at {rect} - {System.IO.Path.GetFileName(clip)}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("VideoService: floating companion could not start - {Error}", ex.Message);
                var half = _companions.FirstOrDefault(x => ReferenceEquals(x.Window, win));
                if (half != null) CloseCompanion(half, "start failed");
                else try { win?.Close(); } catch { }
                return false;
            }
        }

        private async Task InitCompanionAsync(FloatingCompanion c)
        {
            try
            {
                var env = await BrowserVideoEngine.SharedEnvironmentAsync().ConfigureAwait(true);
                if (c.Closed) return;
                if (env == null) { CloseCompanion(c, "no WebView2 environment"); return; }
                await c.Window.InitAsync(env, BrowserVideoEngine.SharedMappings(),
                    BrowserVideoEngine.PlayerStartUrl, BrowserVideoEngine.PlayerHost).ConfigureAwait(true);
                if (!_videoPlaying) CloseFloatingCompanions("the video ended while a companion was starting");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: floating companion init failed - {Error}", ex.Message);
                CloseCompanion(c, "init threw");
            }
        }

        private void OnCompanionMessage(BrowserVideoWindow win, JObject o)
        {
            var c = _companions.FirstOrDefault(x => ReferenceEquals(x.Window, win));
            if (c == null || c.Closed) return;
            // Never acted on inside the WebView2 message callback (same rule as the session).
            switch ((string?)o["type"])
            {
                case "playing":
                    c.Playing = true;
                    break;
                case "time":
                    // The video this window keeps company is gone: so is the window.
                    if (!_videoPlaying)
                        PostAfterPageMessage(() => { if (!_videoPlaying) CloseFloatingCompanions("no video left to keep company"); });
                    break;
                case "ended":
                    PostAfterPageMessage(() => CloseCompanion(c, "clip ended"));
                    break;
                case "error":
                    PostAfterPageMessage(() => CloseCompanion(c, "page error " + ((int?)o["code"] ?? 0)));
                    break;
                case "click":
                    // The page posts this on pointerdown: a touch (the frame reports its own too).
                    PostAfterPageMessage(() => { if (!c.Closed && c.Surface != null) OnFloatingTouched(c.Surface); });
                    break;
            }
        }

        private void OnCompanionInitFailed(BrowserVideoWindow win, string reason)
            => PostAfterPageMessage(() => CloseCompanionOf(win, "WebView2 init failed: " + reason));

        private void OnCompanionProcessFailed(BrowserVideoWindow win, CoreWebView2ProcessFailedKind kind)
        {
            try { BrowserVideoEngine.ReportProcessFailure(kind); } catch (Exception ex) { Diag.Swallowed(ex); }
            PostAfterPageMessage(() => CloseCompanionOf(win, "browser process failed"));
        }

        private void CloseCompanionOf(BrowserVideoWindow win, string reason)
        {
            var c = _companions.FirstOrDefault(x => ReferenceEquals(x.Window, win));
            if (c != null) CloseCompanion(c, reason);
        }

        /// <summary>Close one companion. Idempotent. When it had the sound, the most recent
        /// survivor takes it.</summary>
        private void CloseCompanion(FloatingCompanion c, string reason, bool refocus = true)
        {
            if (c.Closed) return;
            c.Closed = true;
            _companions.Remove(c);
            c.FirstFrameWatch?.Stop();
            if (c.Surface != null) DropSurface(c.Surface);
            var w = c.Window;
            try
            {
                w.Message -= OnCompanionMessage;
                w.InitFailed -= OnCompanionInitFailed;
                w.ProcessFailed -= OnCompanionProcessFailed;
                if (w.IsReady) w.Post(new { type = "stop" });
                w.Close();
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: floating companion close failed - {Error}", ex.Message); }
            VideoDiag.Log("VIDEO", $"floating companion closed ({reason}), {_companions.Count} left");
            if (refocus) ApplyAudioFocus();
        }

        /// <summary>Every companion goes, now. Called first thing by every teardown (CloseAll, and
        /// by hand from Stop / Cleanup / ForceCleanup in case CloseAll declines as a duplicate).
        /// Idempotent, never throws.</summary>
        private void CloseFloatingCompanions(string reason)
        {
            if (_companions.Count == 0) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                try { dispatcher.BeginInvoke(new Action(() => CloseFloatingCompanions(reason))); }
                catch (Exception ex) { Diag.Swallowed(ex); }
                return;
            }
            foreach (var c in _companions.ToList())
            {
                try { CloseCompanion(c, reason, refocus: false); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            try { ApplyAudioFocus(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>The whole floating layer of a video that is being torn down: companions, the
        /// primary's frame, the sound bookkeeping.</summary>
        private void CloseFloatingLayer(string reason)
        {
            try
            {
                CloseFloatingCompanions(reason);
                var primary = _primarySurface;
                _primarySurface = null;
                if (primary != null) DropSurface(primary);
                _audioFocus.Clear();
                _primaryFocusMuted = false;
                _primaryEverFocusMuted = false;
                _primaryAudioTrack = -1;
                _floatPrimaryClip = null;
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: floating layer close failed - {Error}", ex.Message); }
        }

        // ===================== Test button =====================

        /// <summary>
        /// Test pressed while a floating video is up: one more window, up to the "how many" setting;
        /// at the limit the oldest companion makes room. False when this is not that case (full
        /// screen, one window only, nothing visible, no clip the page can play), and the caller
        /// keeps its "a video appears to be playing" dialog.
        /// </summary>
        public bool TryAddFloatingTestVideo()
        {
            try
            {
                if (!_videoPlaying || !_playbackStarted || !IsFloating || _isCleaningUp || _gracePaused) return false;
                if (_primaryVideoWindow == null || !_primaryVideoWindow.IsVisible) return false;
                var room = FloatingWindowCount() - 1;
                if (room <= 0 || !BrowserVideoEngine.Instance.IsAvailable) return false;
                if (_companions.Count >= room) CloseCompanion(_companions[0], "test press, the oldest makes room");
                return SpawnCompanion();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoService: floating test add failed - {Error}", ex.Message);
                return false;
            }
        }
    }
}
