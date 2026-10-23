using System;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    // WPF CompanionIdleVoice: an unprompted bubble keeps its text with the engine off.
    internal static bool ShouldVoice(bool unprompted) => !unprompted || CoreEngine.IsRunning
        || App.Sessions is { IsRunning: true, IsPaused: false } || CoreSettings.Current.CompanionSpeaksIdleWhenEngineOff;
    private DispatcherTimer? _idleTimer;
    internal TimeSpan? IdleInterval => _idleTimer?.Interval;
    internal bool IdleTimerRunning => _idleTimer?.IsEnabled == true;

    /// <summary>WPF StartIdleTimer / RestartIdleTimer: the companion slider owns the cadence.</summary>
    public void RestartIdleTimer() => RunOnAvatar(() =>
    {
        _idleTimer ??= new DispatcherTimer();
        _idleTimer.Stop();
        _idleTimer.Tick -= OnIdleTick;
        _idleTimer.Tick += OnIdleTick;
        _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        _idleTimer.Start();
    });

    private void OnIdleTick(object? sender, EventArgs e) => IdleTick();
    internal void IdleTick()
    {
        if (_idleTimer?.IsEnabled != true) return; // panic/close retired this beat
        _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        if (!IsSpeechReady()) return;
        Platform.BarkHead.Engine?.DispatchIdle();
    }
}
