using System;
using System.Threading;
using Avalonia.Threading;
using Serilog;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Cue and playback-hold slice of WPF Services/Deeper/SpeakPromptSession.cs:123-151,167-225,334-349.
/// The runtime must call Start only after speech availability and mic consent succeed
/// (WPF :58-111), and Dispose on completion or stop. This does not open a microphone.
/// </summary>
internal sealed class SpeakPromptCueSession : IDisposable
{
    private readonly SpeakPromptPlan _plan;
    private readonly Action<string> _flash;
    private readonly CancellationToken _cancellation;
    private readonly IPlaybackTimeSource? _source;
    private CancellationTokenRegistration _registration;
    private DispatcherTimer? _timer, _holdTimer;
    private bool _started, _stopped, _paused;

    internal SpeakPromptCueSession(SpeakPromptPlan plan, Action<string> flash,
        CancellationToken cancellation = default, IPlaybackTimeSource? source = null)
    {
        _plan = plan;
        _flash = flash;
        _cancellation = cancellation;
        _source = source;
    }

    // As in WPF Start/Stop, all cue and timer work belongs to the UI dispatcher.
    internal void Start()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Start); return; }
        if (_started || _stopped || _cancellation.IsCancellationRequested) return;
        _started = true;
        _registration = _cancellation.Register(() =>
        {
            if (Dispatcher.UIThread.CheckAccess()) Dispose();
            else Dispatcher.UIThread.Post(Dispose);
        });
        if (_stopped) return;
        if (_plan.CueMode == SpeakCueMode.Persistent)
            SpeakCueOverlay.ShowCue(_plan.Cue);
        else
        {
            // The caller supplies FlashSubliminalCustom(text, suppressHaptic: true),
            // matching WPF :140-143. Persistent cues never enter that path.
            Pulse();
            if (_stopped || _cancellation.IsCancellationRequested) return;
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(_plan.CueIntervalMs)
            };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        if (!_stopped && !_cancellation.IsCancellationRequested) StartHoldWatch();
    }

    private void OnTick(object? sender, EventArgs e) => Pulse();

    // Also used by deterministic headless tests; the live timer invokes this same method.
    internal void Pulse()
    {
        if (!_started || _stopped || _cancellation.IsCancellationRequested) return;
        if (_plan.CueMode != SpeakCueMode.Intermittent) return;
        try { _flash(_plan.Cue); }
        catch (Exception ex) { Log.Debug("Speak cue flash: {E}", ex.Message); }
    }

    internal TimeSpan? CueInterval => _timer?.Interval;
    internal TimeSpan? HoldInterval => _holdTimer?.Interval;

    // WPF SpeakPromptSession.cs:167-225. A rule-fired pause has no region end,
    // so take it immediately; a region is checked every 120 ms.
    private void StartHoldWatch()
    {
        if (!_plan.HoldsPlayback || _source == null) return;
        if (!_plan.HasRegion)
        {
            if (_plan.PausesAtCue) Pause();
            return;
        }
        _holdTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _holdTimer.Tick += OnHoldTick;
        _holdTimer.Start();
    }

    private void OnHoldTick(object? sender, EventArgs e) => HoldTick();

    internal void HoldTick()
    {
        if (_holdTimer == null || _stopped || _cancellation.IsCancellationRequested || _source == null) return;
        try
        {
            switch (_plan.HoldStepAt(_source.GetCurrentTimeSeconds(), _paused))
            {
                case SpeakHoldStep.SeekToRegionStart:
                    _source.Seek(_plan.RegionStart!.Value);
                    break;
                case SpeakHoldStep.Pause:
                    Pause();
                    break;
            }
        }
        catch (Exception ex) { Log.Debug("Speak hold tick: {E}", ex.Message); }
    }

    private void Pause()
    {
        try { _source!.Pause(); _paused = true; }
        catch (Exception ex) { Log.Debug("Speak hold pause: {E}", ex.Message); }
    }

    public void Dispose()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Dispose); return; }
        if (_stopped) return;
        _stopped = true;
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
        if (_started && _plan.CueMode == SpeakCueMode.Persistent)
            SpeakCueOverlay.HideCue();
        if (_holdTimer != null)
        {
            _holdTimer.Stop();
            _holdTimer.Tick -= OnHoldTick;
            _holdTimer = null;
        }
        if (_paused)
        {
            _paused = false;
            try { _source?.Play(); }
            catch (Exception ex) { Log.Debug("Speak hold release: {E}", ex.Message); }
        }
        _registration.Dispose();
    }
}
