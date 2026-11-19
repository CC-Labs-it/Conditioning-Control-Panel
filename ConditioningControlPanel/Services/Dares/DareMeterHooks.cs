using System;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// The app's hook points into the meter (CONTRACT.md "Counting"): flash images on screen
/// (<c>FlashService.FlashDisplayed</c>, one per image of the batch), bubbles popped
/// (<c>BubbleService.OnBubblePopped</c>), a finished session (<see cref="DareTaps.SessionFinished"/>),
/// and a 1 s sampler for the clocks (engine or session minutes, video minutes) plus the 500 ms camera
/// check. Both timers run only while a bet is inside its window; nothing here costs anything while no
/// bet is counting. Lives on the UI thread.
/// </summary>
public sealed class DareMeterHooks : IDisposable
{
    private readonly DareMeter _meter;
    private readonly Func<DateTimeOffset> _now;
    private readonly DareCameraCheck _camera;
    private DispatcherTimer? _sampler;
    private DispatcherTimer? _cameraTimer;
    private DateTimeOffset _lastSample;
    private bool _attached;
    private bool _disposed;

    public DareMeterHooks(DareMeter meter, Func<DateTimeOffset>? now = null, IDareCameraProbe? camera = null)
    {
        _meter = meter;
        _now = now ?? (() => ServerClock.UtcNow);
        _camera = new DareCameraCheck(camera ?? new AppDareCameraProbe());
    }

    public DareCameraState CameraState => _camera.State;

    /// <summary>What is running now: the engine, a session, any video (one answer for any number of them).</summary>
    public static DareLive AppLive()
    {
        bool video = false;
        try
        {
            var v = App.Video;
            if (v != null)
                video = (v.IsPlaying && !v.IsGracePaused && v.IsPrimaryMediaPlaying) || v.HasFloatingInstances;
            if (!video) video = App.BrowserMedia?.IsPlaying == true;
        }
        catch { video = false; }
        return new DareLive(App.IsEngineRunning, App.IsSessionRunning, video);
    }

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        try { if (App.Flash != null) App.Flash.FlashDisplayed += OnFlash; } catch (Exception ex) { App.Logger?.Debug("Dares flash hook failed: {E}", ex.Message); }
        try { if (App.Bubbles != null) App.Bubbles.OnBubblePopped += OnPop; } catch (Exception ex) { App.Logger?.Debug("Dares bubble hook failed: {E}", ex.Message); }
        DareTaps.SessionFinished += OnSession;
    }

    /// <summary>The bets changed: start the timers if anything is inside its window.</summary>
    public void Wake()
    {
        if (_disposed) return;
        if (!_meter.AnyInWindow(_now())) return;
        if (_sampler == null)
        {
            _lastSample = _now();
            _sampler = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _sampler.Tick += (_, _) => SampleTick();
            _sampler.Start();
        }
    }

    private void SampleTick()
    {
        var now = _now();
        if (!_meter.AnyInWindow(now))
        {
            _sampler?.Stop();
            _sampler = null;
            StopCamera();
            _meter.Save();
            return;
        }
        var live = AppLive();
        _meter.Sample((now - _lastSample).TotalSeconds, live, now);
        _lastSample = now;
        if (_cameraTimer == null && _meter.CameraWanted(now, live))
        {
            _cameraTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(DareGrammar.CameraSampleMs) };
            _cameraTimer.Tick += (_, _) =>
            {
                if (!_camera.Step(_meter, AppLive(), _now())) StopCamera();
            };
            _cameraTimer.Start();
        }
    }

    private void StopCamera()
    {
        _cameraTimer?.Stop();
        _cameraTimer = null;
        try { _camera.Step(_meter, default, _now()); } catch { }
    }

    private void OnFlash(object? sender, EventArgs e)
    {
        try
        {
            var n = App.Flash?.LastDisplayedImagePaths.Count ?? 0;
            _meter.NotePics(Math.Max(1, n), _now());
        }
        catch (Exception ex) { App.Logger?.Debug("Dares flash count failed: {E}", ex.Message); }
    }

    private void OnPop()
    {
        try { _meter.NoteBubbles(1, _now()); }
        catch (Exception ex) { App.Logger?.Debug("Dares bubble count failed: {E}", ex.Message); }
    }

    private void OnSession(Models.SessionDifficulty d)
    {
        _meter.NoteSession(d, _now());
        _meter.Save();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sampler?.Stop();
        _sampler = null;
        StopCamera();
        try { if (App.Flash != null) App.Flash.FlashDisplayed -= OnFlash; } catch { }
        try { if (App.Bubbles != null) App.Bubbles.OnBubblePopped -= OnPop; } catch { }
        DareTaps.SessionFinished -= OnSession;
        _meter.Save();
    }
}
