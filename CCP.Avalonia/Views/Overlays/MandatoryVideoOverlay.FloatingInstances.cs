using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
        public WriteableBitmap? Fill;
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
                f.Playback.SetAudioOff(true);   // WPF FloatingInstances.cs:534-535: veiled, it takes no sound until the reveal
                f.FrameSeen = true;
                RevealInstance(f);
            }
            else
            {
                if (LibVlcAudio.Shared is not { } vlc) { CloseInstance(f, "LibVLC is not available", endRun: false); return false; }
                var s = CoreSettings.Current;
                var player = f.Player = new MediaPlayer(vlc) { EnableHardwareDecoding = s.VideoForceHardwareDecoding };
                f.Playback = new NativeMandatoryPlayback(player);
                f.Playback.SetAudioOff(true);   // WPF :534-535 (WatchInstance re-applies once the track exists)
                ApplyInstanceVolume(f);
                f.Sink = new VlcFrameSink(player, () => f.Media, bmp => f.Surface.Video.Source = bmp, () =>
                {
                    f.FrameSeen = true;
                    if (f.Sink?.Bitmap is { } frame) UpdateFloatingFill(f, frame, s.VideoBlurredBackgroundEnabled);
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
        // Veiled or not the holder: silent, also for an audio track LibVLC selects after the last focus pass.
        p.SetAudioOff(InstanceAudioOff(f));
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

    /// <summary>WPF FloatingInstances.cs:514: each joined clip supplies its own blurred background.</summary>
    internal static void UpdateFloatingFill(FloatingInstance f, WriteableBitmap frame, bool enabled)
    {
        if (f.Closed) return;
        var x = f.Surface;
        var aspect = (double)frame.PixelSize.Width / frame.PixelSize.Height;
        var bounds = x.Window.Bounds;
        var on = enabled && MandatoryVideoScheduler.NeedsBlurFill(aspect,
            bounds.Width > 0 && bounds.Height > 0 ? bounds.Width / bounds.Height : 16.0 / 9);
        x.Fill.IsVisible = x.Scrim.IsVisible = on;
        if (!on)
        {
            x.Fill.Source = null;
            f.Fill?.Dispose();
            f.Fill = null;
            return;
        }
        var height = Math.Max(1, (int)Math.Round(FillWidth / aspect));
        if (f.Fill == null || f.Fill.PixelSize.Height != height)
        {
            x.Fill.Source = null;
            f.Fill?.Dispose();
            f.Fill = new WriteableBitmap(new PixelSize(FillWidth, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            x.Fill.Source = f.Fill;
        }
        Downscale(frame, f.Fill);
        x.Fill.InvalidateVisual();
    }

    /// <summary>WPF RevealInstance (:593-617): shown, it joins the sound rotation; controls after the fade.</summary>
    private void RevealInstance(FloatingInstance f)
    {
        if (f.Closed || f.Revealed) return;
        f.Revealed = true;
        // WPF :613: focus first, so a window that does not take the sound is never heard when the reveal lifts its hold.
        _audioFocus.Join(f.Window, CoreSettings.Current.MandatoryVideoFloatingKeepSound);
        ApplyAudioFocus();
        // Keep watching Lockdown, and keep the existing picture fade running during the grow.
        // Controls need both the picture fade and WPF's reveal callback to finish.
        WatchForLocks();
        bool revealed = false, faded = false;
        void ShowWhenReady() { if (revealed && faded) ShowInstanceControls(f); }
        var w = f.Window;
        f.Fade = new VideoReveal(_videoFadeSeconds, tick => w.RequestAnimationFrame(_ => tick()),
            opacity => f.Surface.Picture.Opacity = opacity, () => { faded = true; ShowWhenReady(); });
        if (f.Surface.Reveal is { } reveal)
            reveal.Present(() => { revealed = true; ShowWhenReady(); });
        else revealed = true;
        if (!f.Closed) f.Fade.Start();
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

    /// <summary>LibVLC volume is shared by the whole process on Windows (one audio session for every player), so a
    /// joined window's own mute and its reveal hold go through its audio track, like WPF ApplyPrimaryAudioTrack
    /// (VideoService.FloatingInstances.cs:74-75, :378-401); the volume only follows Settings, the same for all.</summary>
    private void ApplyInstanceVolume(FloatingInstance f)
    {
        if (f.Player is { } p)
            p.Volume = MandatoryVideoScheduler.EffectiveVolume(CoreSettings.Current.MasterVolume, CoreSettings.Current.VideoVolume);
        if (f.Surface is null) return;   // the reveal holds the sound while the window is still being built
        try { f.Playback?.SetAudioOff(InstanceAudioOff(f)); }
        catch (Exception ex) { Log.Debug("VideoService: floating audio switch failed - {Error}", ex.Message); }
    }

    /// <summary>Silent unless it holds the focus, is not muted by its own button and is not held by its reveal.</summary>
    private bool InstanceAudioOff(FloatingInstance f) => f.UserMuted || f.Held || !_audioFocus.HasSound(f.Window);

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
            try { if (_audioFocus.Items.Contains(f.Window)) f.Playback?.SetAudioOff(InstanceAudioOff(f)); }
            catch (Exception ex) { Log.Debug("VideoService: floating audio switch failed - {Error}", ex.Message); }
        }
        _refreshFloating?.Invoke();
    }

    /// <summary>WPF VideoService.cs:6444-6456: one Resume card per screen holding a floating window. A card is
    /// hosted by a window on that screen; WPF's cards outlive the windows, so a closed host hands it on.</summary>
    private void ShowGraceCards()
    {
        foreach (var screen in AllSurfaces.GroupBy(x => x.Screen?.Bounds).Where(g => g.All(x => x.GraceHost == null)))
        {
            var x = screen.First();
            x.Layer.IsHitTestVisible = false;
            x.Grace.IsVisible = true;
            ShowGraceHost(x);
        }
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
        // WPF FloatingInstances.cs:417-430: a grace pause always ends in a resume, and the resume closes every
        // screen's card (WPF VideoService.cs:6325-6335); the joined windows play on, the grace spent.
        ResumeFromGrace("the first window closed");
        var watched = ClosePrimary(retire: true);
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
        try { f.Surface?.Reveal?.Cancel(); } // stop animation before joining a decoder thread
        catch (Exception ex) { Log.Debug("VideoService: floating reveal cancel failed - {Error}", ex.Message); }
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
            if (_gracePaused && endRun) ShowGraceCards();   // its screen's Resume card moves to a window still up
            x.Video.Source = null;
            x.Fill.Source = null;
            x.Fill.IsVisible = x.Scrim.IsVisible = false;
            x.Window.Close();
        }
        f.Sink?.Free();
        f.Sink = null;
        f.Fill?.Dispose();
        f.Fill = null;
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
