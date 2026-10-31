using System;
using System.Threading;
using Avalonia.Threading;
using Serilog;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Cue-only slice of WPF Services/Deeper/SpeakPromptSession.cs:123-151,334-349.
/// The runtime must call Start only after speech availability and mic consent succeed
/// (WPF :58-111). This does not open a microphone or hold playback.
/// </summary>
internal sealed class SpeakPromptCueSession : IDisposable
{
    private readonly SpeakPromptPlan _plan;
    private readonly Action<string> _flash;
    private readonly CancellationToken _cancellation;
    private CancellationTokenRegistration _registration;
    private DispatcherTimer? _timer;
    private bool _started, _stopped;

    internal SpeakPromptCueSession(SpeakPromptPlan plan, Action<string> flash,
        CancellationToken cancellation = default)
    {
        _plan = plan;
        _flash = flash;
        _cancellation = cancellation;
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
        _registration.Dispose();
        if (_started && _plan.CueMode == SpeakCueMode.Persistent)
            SpeakCueOverlay.HideCue();
    }
}
