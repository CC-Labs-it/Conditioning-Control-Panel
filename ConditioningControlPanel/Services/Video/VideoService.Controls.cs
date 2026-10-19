using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Services.Video;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The mute and close buttons on every mandatory video (owner, 2026-10-19). The buttons are
    /// <see cref="VideoControlsOverlay"/>, one per video window: each screen's copy of a full-screen
    /// video, the floating LibVLC window, and each floating browser instance. Shown once the clip
    /// is playing, gone with their window (and first thing in CloseAll).
    ///
    ///   * CLOSE is the Esc door with a mouse, minus the grace pause: <c>Cleanup()</c>, exactly what
    ///     a non-strict Esc does (SetupStrictHandlers, TryEscapeFromGlobalKey, HandleBrowserKey), so
    ///     timers, the duck, the queue slot, VideoEnded and the next scheduling run as on any
    ///     dismiss. It is a skip, not a watch: no attention-check XP, no Circe "to the end" note
    ///     (both live only in EndCurrentVideo), watch time for what played. A floating instance
    ///     closes alone through <c>CloseInstance</c> (watchedToEnd false). Hidden under any lock
    ///     (<see cref="MandatoryVideoControls.ShowClose"/>), taken down when a Lockdown starts
    ///     mid-video, and refused again on the press.
    ///   * MUTE silences THIS video run only: folded into <see cref="GetEffectiveVolume"/> beside the
    ///     DtRH external mute, so every player follows the one existing path (LibVLC Volume/Mute,
    ///     MediaElement Volume, the browser page's muted flag, floating windows on top of their audio
    ///     focus). Never the Windows mixer. Cleared when the video ends (Cleanup / ForceCleanup /
    ///     Stop), so the next video has sound; an attention-check replay (a fresh clip that skips
    ///     Cleanup) stays muted. A press during a teardown does nothing.
    ///   * Neither button shows during a Chaos or Down the Rabbit Hole run: the run draws its own HUD
    ///     over the clip and the dive holds its own mute (owner, 2026-10-19).
    /// </summary>
    public partial class VideoService
    {
        private readonly List<VideoControlsOverlay> _videoControls = new();
        private volatile bool _userMuted;
        private object? _lockWatchOwner;

        /// <summary>A Chaos run or a Down the Rabbit Hole descent: the video covers the screen on
        /// purpose (no float, no buttons, no credit link). One answer for every rule that asks.</summary>
        internal static bool LockedChaosRun()
            => App.Chaos?.IsRunning == true || ConditioningControlPanel.Services.Chaos.DtrhHostService.IsRunActive;

        private bool CloseAllowed(bool strict)
            => MandatoryVideoControls.ShowClose(strict || IsStrictActive, App.Lockdown?.IsActive == true, LockedChaosRun());

        private bool ControlsAllowed(bool strict)
            => MandatoryVideoControls.ShowMute(strict || IsStrictActive, App.Lockdown?.IsActive == true, LockedChaosRun());

        /// <summary>Buttons on every window of the single video on screen that has none yet. UI thread.</summary>
        private void ShowVideoControls(bool strict, int delayMs = 0)
        {
            try
            {
                if (!_videoPlaying || _isCleaningUp) return;
                if (!ControlsAllowed(strict)) return;
                var showClose = CloseAllowed(strict);
                foreach (var w in _windows.ToList())
                    AttachVideoControls(w, showClose, () => CloseVideoFromButton(strict), delayMs);
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: video controls failed - {Error}", ex.Message); }
        }

        private void AttachVideoControls(Window host, bool showClose, Action onClose, int delayMs = 0)
        {
            if (_videoControls.Any(c => ReferenceEquals(c.Host, host))) return;
            WatchForLocks();
            var c = new VideoControlsOverlay(host, showClose, _userMuted, ToggleUserMute, onClose, delayMs);
            c.Closed += x => _videoControls.Remove(x);
            if (!c.IsClosed) _videoControls.Add(c);
        }

        private void ToggleUserMute()
        {
            // Same guard as the X: a press that lands while CloseAll pumps the dispatcher must not
            // leave the flag set after the reset, or the next video would start silent.
            if (!_videoPlaying || _isCleaningUp) return;
            _userMuted = !_userMuted;
            VideoDiag.Log("VIDEO", $"mute button - video {(_userMuted ? "muted" : "unmuted")}");
            UpdatePlayingVideosVolume();
            foreach (var c in _videoControls.ToList()) c.SetMuted(_userMuted);
        }

        /// <summary>A lock started mid-video (a Lockdown): the X that now refuses goes away everywhere.</summary>
        private void HideCloseButtons()
        {
            foreach (var c in _videoControls.ToList()) c.HideClose();
        }

        /// <summary>Every button window goes now. Called first thing by CloseAll. Never throws.</summary>
        private void CloseVideoControls()
        {
            try
            {
                if (_videoControls.Count == 0) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(CloseVideoControls));
                    return;
                }
                foreach (var c in _videoControls.ToList()) c.Close();
                _videoControls.Clear();
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>A Lockdown that starts while the buttons are up takes the X down at once. Strict
        /// itself is fixed per clip (PlayVideo), so the Lockdown is the lock that can arrive mid-video.
        /// Subscribed once per Lockdown service, lazily: App.Lockdown may come up after this service.</summary>
        private void WatchForLocks()
        {
            var lockdown = App.Lockdown;
            if (lockdown == null || ReferenceEquals(_lockWatchOwner, lockdown)) return;
            _lockWatchOwner = lockdown;
            lockdown.LockdownActivated += OnLockdownActivated;
        }

        private void OnLockdownActivated()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    VideoDiag.Log("VIDEO", "Lockdown started - the close button goes");
                    HideCloseButtons();
                }));
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>The video ended: the next one starts with its sound.</summary>
        private void ResetUserMute() => _userMuted = false;

        /// <summary>The X on a full-screen (or LibVLC floating) video: the non-strict Esc dismiss.</summary>
        private void CloseVideoFromButton(bool strict)
        {
            if (!_videoPlaying || _isCleaningUp) return;
            if (!CloseAllowed(strict))
            {
                VideoDiag.Log("PANIC", "close button refused - the video is locked");
                HideCloseButtons();
                return;
            }
            VideoDiag.Log("PANIC", "close button - dismissing the video via Cleanup (the Esc door)");
            Cleanup();
        }

        /// <summary>The X on one floating browser window: that window goes, the run carries on.</summary>
        private void ShowInstanceControls(Video.Browser.BrowserVideoWindow win, int delayMs)
        {
            try
            {
                if (!_videoPlaying || _isCleaningUp) return;
                if (!ControlsAllowed(strict: false)) return;
                AttachVideoControls(win, CloseAllowed(strict: false), () =>
                {
                    if (!CloseAllowed(strict: false)) { HideCloseButtons(); return; }
                    VideoDiag.Log("PANIC", "close button - one floating window closes (CloseInstance)");
                    CloseInstanceOf(win, "close button");
                }, delayMs);
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: floating controls failed - {Error}", ex.Message); }
        }
    }
}
