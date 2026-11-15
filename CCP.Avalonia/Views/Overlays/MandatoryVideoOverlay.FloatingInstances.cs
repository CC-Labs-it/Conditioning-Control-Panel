using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF VideoService.FloatingInstances.cs:18-130: the run's clip (Show) is the first floating window; later triggers
/// join windows with their own LibVLC player, frame and reveal, up to MandatoryVideoFloatingCount. A clip's end closes only
/// its own window with its own credit; the last one ends the run. Which window has the sound is
/// <see cref="FloatingAudioFocus{T}"/>. Every teardown (CloseAll: panic, Esc, stop) closes all.</summary>
internal sealed partial class MandatoryVideoOverlay
{
    internal sealed class FloatingInstance(string clip)
    {
        public string Clip { get; } = clip;
        public Surface Surface { get; set; } = null!;
        public Window Window => Surface.Window;
        public IMandatoryPlayback? Playback { get; set; }
        public MediaPlayer? Player;
        public Media? Media;
        public VlcFrameSink? Sink;
        public DispatcherTimer? Watch;
        public VideoReveal? Fade;
        public readonly Stopwatch SinceReady = new();
        public long StartMs, PlayedMs;
        public bool FrameSeen, Revealed, Closed, UserMuted, Held, CutStartChosen;
    }

    /// <summary>Joined windows, oldest first; the run's own clip is older than all of them. UI thread only.</summary>
    private readonly List<FloatingInstance> _floaters = new();
    private readonly FloatingAudioFocus<Window> _audioFocus = new();

    internal IReadOnlyList<FloatingInstance> Floaters => _floaters;
    private IEnumerable<Surface> AllSurfaces => _surfaces.Concat(_floaters.Select(f => f.Surface));

    public int FloatingLimit => MandatoryVideoPresentation.ClampCount(CoreSettings.Current.MandatoryVideoFloatingCount);
    /// <summary>Floating windows on screen: the run's own (while it plays) and the joined ones.</summary>
    public int FloatingUp => (Floating ? _surfaces.Count : 0) + _floaters.Count;
    /// <summary>WPF _floatRunOpen: the windows own the run; the last one to close ends it.</summary>
    public bool FloatingRunOpen => Scheduler.IsPlaying && FloatingUp > 0;

    internal bool HasFloatingSound(Window w) => _audioFocus.HasSound(w);

    /// <summary>WPF TryFloatingTrigger (:109-118) and TryAddFloatingTestVideo (:872-904): a trigger while a
    /// floating video plays adds a window (or skips quietly at the limit); a Test press replaces the oldest.</summary>
    public bool TryFloatingTrigger(bool userInitiated)
    {
        if (!Floating && _floaters.Count == 0) return false;
        if (FloatingLimit < 2 && _floaters.Count == 0)
        {
            // One window is all there may be: a Test press replaces it (WPF :896), an automatic trigger waits.
            if (!userInitiated) return false;
            Scheduler.ForceCleanup();
            return Scheduler.Trigger(userInitiated: true);
        }
        AddFloatingInstance(replaceOldestAtLimit: userInitiated);
        return true;
    }

    /// <summary>WPF AddFloatingInstance (:122-150).</summary>
    private bool AddFloatingInstance(bool replaceOldestAtLimit)
    {
        try
        {
            if (!Scheduler.IsPlaying || _closing || _gracePaused || !Floats(strict: false)) return false;
            var decision = MandatoryVideoPresentation.DecideTrigger(FloatingUp, FloatingLimit, replaceOldestAtLimit);
            if (decision == MandatoryVideoPresentation.TriggerDecision.Skip)
            {
                Log.Information("VideoService: floating trigger skipped - {Up} up, that is the limit", FloatingUp);
                return false;
            }
            var clip = PickFloatingClip();
            if (clip == null) return false;
            if (decision == MandatoryVideoPresentation.TriggerDecision.ReplaceOldest)
                while (FloatingUp >= FloatingLimit && FloatingUp > 0)
                {
                    if (Floating && _surfaces.Count > 0) RetirePrimary("the oldest makes room", endRun: false);
                    else CloseInstance(_floaters[0], "the oldest makes room", endRun: false);
                }
            var ok = SpawnInstance(clip);
            if (decision == MandatoryVideoPresentation.TriggerDecision.Open && SpawnTogetherExtras() > 0) ok = true;
            if (decision == MandatoryVideoPresentation.TriggerDecision.ReplaceOldest && FloatingUp == 0) Scheduler.End();
            return ok;
        }
        catch (Exception ex)
        {
            Log.Debug("VideoService: floating add failed - {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>WPF SpawnTogetherExtras (:255-269): "Pop up together" fills every free slot at once.</summary>
    private int SpawnTogetherExtras()
    {
        if (!CoreSettings.Current.MandatoryVideoFloatingTogether) return 0;
        var want = MandatoryVideoPresentation.WindowsToOpen(FloatingUp, FloatingLimit, together: true);
        var opened = 0;
        for (var i = 0; i < want && Scheduler.IsPlaying; i++)
        {
            var clip = PickFloatingClip();
            if (clip == null || !SpawnInstance(clip)) break;
            opened++;
        }
        if (opened > 0) Log.Information("VideoService: floating: {Opened} more opened together ({Up} up)", opened, FloatingUp);
        return opened;
    }

    /// <summary>WPF PickFloatingClip (:414-430): not a clip already up while the pool allows.</summary>
    private string? PickFloatingClip()
    {
        var up = new HashSet<string>(_floaters.Select(f => f.Clip), StringComparer.OrdinalIgnoreCase);
        if (_surfaces.Count > 0) up.Add(_source);
        string? repeat = null;
        for (var i = 0; i < 6; i++)
        {
            var p = Scheduler.PickNext();
            if (p == null) break;
            if (!up.Contains(p)) return p;
            repeat ??= p;
        }
        return repeat;
    }

    /// <summary>WPF SpawnInstance (:444-531): its own player and window, veiled until its first frame.</summary>
    private bool SpawnInstance(string clip)
    {
        var play = Scheduler.PlaybackPath(clip);
        if (play == null) return false;
        var f = new FloatingInstance(clip);
        try
        {
            var host = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var screens = Targets(host, fillAll: true);
            f.Surface = Build(screens[_random.Next(screens.Count)], strict: false, f);
            _floaters.Add(f);
            f.SinceReady.Start();
            if (_playbackFactory != null)
            {
                f.Playback = _playbackFactory();
                f.FrameSeen = true;
                RevealInstance(f);
            }
            else
            {
                if (LibVlcAudio.Shared is not { } vlc) { CloseInstance(f, "LibVLC is not available", endRun: false); return false; }
                var s = CoreSettings.Current;
                var player = f.Player = new MediaPlayer(vlc) { EnableHardwareDecoding = s.VideoForceHardwareDecoding };
                f.Playback = new NativeMandatoryPlayback(player);
                ApplyInstanceVolume(f);
                f.Sink = new VlcFrameSink(player, () => f.Media, bmp => f.Surface.Video.Source = bmp, () =>
                {
                    f.FrameSeen = true;
                    f.Surface.Video.InvalidateVisual();
                });
                player.Playing += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    if (f.Closed) return;
                    LibVlcAudio.ApplyPreferredDevice(player);
                    player.SetRate(MandatoryVideoPlaybackRules.PlaybackRate(s.GentleVisuals, s.GentleSpeed));
                    _refreshFloating?.Invoke();
                });
                // Never torn down on LibVLC's own thread.
                player.EndReached += (_, _) => Dispatcher.UIThread.Post(() => CloseInstance(f, "clip ended", watchedToEnd: true));
                player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() => CloseInstance(f, "player error"));
                f.Media = new Media(vlc, play, VideoSelectionRules.IsRemoteMediaPath(play) ? FromType.FromLocation : FromType.FromPath);
                foreach (var option in RemoteVideoOptions.For(play)) f.Media.AddOption(option);
                if (MandatoryVideoScheduler.SoftwareDecodeOption(s.VideoForceHardwareDecoding) is { } sw) f.Media.AddOption(sw);
                player.Play(f.Media);
            }
            f.Watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            f.Watch.Tick += (_, _) => WatchInstance(f);
            f.Watch.Start();
            Log.Information("VideoService: floating window {Up} of {Limit} up - {File}", FloatingUp, FloatingLimit, Path.GetFileName(clip));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("VideoService: floating window could not start - {Error}", ex.Message);
            CloseInstance(f, "start failed", endRun: false);
            return false;
        }
    }

    /// <summary>WPF CheckReveal (:563-591) and the "time" message (:695-711): reveal or skip at the first-frame
    /// budget, the random start inside the cut, and the longest-video limit per window.</summary>
    private void WatchInstance(FloatingInstance f)
    {
        if (f.Closed || f.Playback is not { } p) { f.Watch?.Stop(); return; }
        if (_gracePaused) return;   // SetFloatingPaused holds its clocks
        if (!f.Revealed)
            switch (MandatoryVideoPresentation.DecideFirstFrame(f.SinceReady.Elapsed.TotalSeconds, f.FrameSeen, p.IsPlaying))
            {
                case MandatoryVideoPresentation.FirstFrameVerdict.Reveal: RevealInstance(f); break;
                case MandatoryVideoPresentation.FirstFrameVerdict.Skip: CloseInstance(f, "no first frame in time"); return;
            }
        var s = CoreSettings.Current;
        if (!f.CutStartChosen && p.IsPlaying && p.IsSeekable && p.Length > 0)
        {
            f.CutStartChosen = true;
            var start = s.VideoCutRandomStart ? VideoLengthCapRules.StartOffsetMs(s.VideoMaxDurationSeconds, p.Length, CutRandom) : 0;
            if (start > 0) { p.Time = f.StartMs = start; return; }
        }
        SampleInstance(f);
        if (s.VideoMaxDurationSeconds > 0 && f.PlayedMs >= s.VideoMaxDurationSeconds * 1000L)
            CloseInstance(f, "longest-video limit reached");
    }

    private static void SampleInstance(FloatingInstance f)
    {
        if (f.Playback is { } p) f.PlayedMs = Math.Max(f.PlayedMs, p.Time - f.StartMs);
    }

    /// <summary>WPF RevealInstance (:593-617): shown, it joins the sound rotation; controls after the fade.</summary>
    private void RevealInstance(FloatingInstance f)
    {
        if (f.Closed || f.Revealed) return;
        f.Revealed = true;
        f.Surface.Reveal?.Present();
        _audioFocus.Join(f.Window, CoreSettings.Current.MandatoryVideoFloatingKeepSound);
        ApplyAudioFocus();
        var w = f.Window;
        f.Fade = new VideoReveal(_videoFadeSeconds, tick => w.RequestAnimationFrame(_ => tick()),
            opacity => f.Surface.Picture.Opacity = opacity, () => ShowInstanceControls(f));
        f.Fade.Start();
    }

    private void ShowInstanceControls(FloatingInstance f)
    {
        if (f.Closed || !ControlsAllowed) return;
        WatchForLocks();   // WPF VideoService.Controls.cs:66
        f.Surface.Buttons.Close.IsVisible = CloseAllowed;
        f.Surface.Buttons.SetMuted(f.UserMuted);
        f.Surface.Buttons.Row.IsVisible = true;
    }

    private void ToggleInstanceMute(FloatingInstance f)
    {
        if (f.Closed) return;
        f.UserMuted = !f.UserMuted;
        ApplyInstanceVolume(f);
        f.Surface.Buttons.SetMuted(f.UserMuted);
        _refreshFloating?.Invoke();
    }

    private static void ApplyInstanceVolume(FloatingInstance f)
    {
        if (f.Player is { } p) p.Volume = f.UserMuted || f.Held ? 0
            : MandatoryVideoScheduler.EffectiveVolume(CoreSettings.Current.MasterVolume, CoreSettings.Current.VideoVolume);
    }

    /// <summary>WPF OnFloatingTouched (:256-260): a pressed window takes the sound.</summary>
    internal void TouchFloating(Window w)
    {
        if (_audioFocus.Touch(w)) ApplyAudioFocus();
    }

    /// <summary>WPF ApplyAudioFocus (:263-276): exactly one window unmuted and lit, the holder.</summary>
    private void ApplyAudioFocus()
    {
        var primary = Floating && _surfaces.Count > 0 ? _surfaces[0].Window : null;
        try { if (primary != null && _audioFocus.Items.Contains(primary)) _playback?.SetAudioOff(!_audioFocus.HasSound(primary)); }
        catch (Exception ex) { Log.Debug("VideoService: floating audio switch failed - {Error}", ex.Message); }
        foreach (var f in _floaters)
        {
            try { if (_audioFocus.Items.Contains(f.Window)) f.Playback?.SetAudioOff(!_audioFocus.HasSound(f.Window)); }
            catch (Exception ex) { Log.Debug("VideoService: floating audio switch failed - {Error}", ex.Message); }
        }
        _refreshFloating?.Invoke();
    }

    /// <summary>WPF SetFloatingPaused (:366-376): the grace pause holds every joined window.</summary>
    private void SetFloatingPaused(bool paused)
    {
        foreach (var f in _floaters)
        {
            try { f.Playback?.SetPause(paused); } catch (Exception ex) { Log.Debug("VideoService: floating pause failed - {Error}", ex.Message); }
            if (paused) f.SinceReady.Stop(); else f.SinceReady.Start();
        }
    }

    /// <summary>The run's own clip goes; the joined windows play on, unless it was the last.</summary>
    private void EndPrimary(string reason)
    {
        if (_floaters.Count > 0) RetirePrimary(reason);
        else Scheduler.End();
    }

    private void RetirePrimary(string reason, bool endRun = true)
    {
        var held = _gracePaused;
        var watched = ClosePrimary(retire: true);   // ends the grace pause with the first window's card
        // WPF FloatingInstances.cs:417-430: a grace pause always ends in a resume; the joined windows play on.
        if (held) { _graceConsumed = true; SetFloatingPaused(false); }   // one grace per run (WPF VideoService.cs:6328-6329)
        if (watched >= 1) CoreProgression.TrackVideoWatched(watched);   // WPF CreditInstance (:779-797)
        Log.Information("VideoService: floating window closed ({Reason}), {Left} left", reason, FloatingUp);
        ApplyAudioFocus();
        if (endRun && FloatingUp == 0 && Scheduler.IsPlaying) Scheduler.End();
    }

    /// <summary>WPF CloseInstance (:799-860): idempotent; credits its own clip, takes nothing with it, and
    /// only the last window to go ends the run.</summary>
    private void CloseInstance(FloatingInstance f, string reason, bool watchedToEnd = false, bool endRun = true, bool refocus = true)
    {
        if (f.Closed) return;
        f.Closed = true;
        _floaters.Remove(f);
        f.Watch?.Stop();
        f.Fade?.Dispose();
        try
        {
            SampleInstance(f);
            if (watchedToEnd && CoreSettings.Current.VideoMaxDurationSeconds <= 0 && f.Playback is { Length: > 0 } p)
                f.PlayedMs = Math.Max(f.PlayedMs, p.Length - f.StartMs);
        }
        catch (Exception ex) { Log.Debug("VideoService: floating position read failed - {Error}", ex.Message); }
        if (f.PlayedMs >= 1000) CoreProgression.TrackVideoWatched(f.PlayedMs / 1000.0);
        if (watchedToEnd) App.ChasterNote("video");
        // As CloseAll: Stop joins the decoder thread, so no frame callback outlives the buffer.
        try { f.Playback?.Stop(); } catch { }
        try { f.Playback?.Dispose(); } catch { }
        try { f.Media?.Dispose(); } catch { }
        f.Playback = null;
        f.Player = null;
        f.Media = null;
        if (f.Surface is { } x)
        {
            _audioFocus.Remove(x.Window);
            CloseGraceHost(x);
            x.Video.Source = null;
            x.Reveal?.Cancel();
            x.Window.Close();
        }
        f.Sink?.Free();
        f.Sink = null;
        Log.Information("VideoService: floating window closed ({Reason}), {Left} left", reason, FloatingUp);
        if (refocus) ApplyAudioFocus();
        if (endRun && FloatingUp == 0 && Scheduler.IsPlaying) Scheduler.End();
    }

    /// <summary>WPF CloseFloatingInstances (:862-890): every joined window goes now. Never throws.</summary>
    private void CloseFloatingInstances(string reason)
    {
        foreach (var f in _floaters.ToList())
        {
            try { CloseInstance(f, reason, endRun: false, refocus: false); }
            catch (Exception ex) { Log.Debug("VideoService: floating close failed - {Error}", ex.Message); }
        }
    }
}
