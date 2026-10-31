using System;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF OverlayService.PulseOverlays: double active overlays for one second, then
/// restore current settings/session opacity (#535). No settings writes and no new windows.
/// ponytail: Brain Drain has no overlay on this base, so it cannot participate yet.</summary>
internal static class OverlayPulse
{
    internal static TimeProvider Clock = TimeProvider.System;
    private static BeatLoop? _timer;   // rides an overlay window's shared beat; parks (and cancels) when it hides
    private static DateTimeOffset _started;
    private static global::Avalonia.Controls.Window? _owner;
    private static EventHandler? _closed;
    private static bool _pink, _spiral, _gentle;
    private static double _riseMs, _fadeMs;
    internal static bool IsPulsing => _timer != null;
    private static bool ServiceRunning => PinkFilterOverlay.PulseHold || CoreSession.IsEngineRunning
        || App.Sessions is { IsRunning: true, IsPaused: false };

    internal static void PulseOverlays()
    {
        if (!ServiceRunning) return;
        var s = CoreSettings.Current;
        var pink = s.PinkFilterEnabled && PinkFilterOverlay.IsShowing;
        var spiral = s.SpiralEnabled && SpiralOverlay.IsShowing;
        if (!pink && !spiral) return;
        Cancel();
        (_pink, _spiral) = (pink, spiral);
        _gentle = GentleVisuals.On;
        _fadeMs = GentleVisuals.FadeMs(0);
        _riseMs = GentleVisuals.RiseMs((int)_fadeMs);
        _started = Clock.GetUtcNow();
        global::Avalonia.Controls.Window owner = pink ? PinkFilterOverlay.Shown[0] : SpiralOverlay.Shown[0];
        var loop = _timer = new BeatLoop(owner, _ => Tick()) { OnParked = Cancel };
        _owner = owner;
        _closed = (_, _) => { if (_timer == loop) Cancel(); };
        owner.Closed += _closed;   // a closed owner beats no more: restore the other overlay now
        Apply(_gentle ? 0 : 1);
        _timer.Start();
    }

    internal static void Tick()
    {
        if (_timer == null) return;
        var ms = (Clock.GetUtcNow() - _started).TotalMilliseconds;
        var total = _gentle ? GentleFade.PulseTotalMs(_riseMs, 1000, _fadeMs) : 1000;
        if (ms >= total || !ServiceRunning) { Cancel(); return; }
        Apply(_gentle ? GentleFade.PulseEnvelope(ms, _riseMs, 1000, _fadeMs) : 1);
    }

    private static void Apply(double? weight)
    {
        if (_pink) PinkFilterOverlay.SetPulse(CoreSettings.Current.PinkFilterEnabled && PinkFilterOverlay.IsShowing ? weight : null);
        if (_spiral) SpiralOverlay.SetPulse(CoreSettings.Current.SpiralEnabled && SpiralOverlay.IsShowing ? weight : null);
    }

    internal static void Cancel()
    {
        if (_owner != null) _owner.Closed -= _closed;
        _owner = null;
        _closed = null;
        _timer?.Stop();
        _timer = null;
        Apply(null);
        _pink = _spiral = false;
    }
}
