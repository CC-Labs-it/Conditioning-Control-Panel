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
        ClearStaleSpeechLatch();   // WPF Speech.cs:1614: this beat is the only one left once she is wedged
        if (!IsSpeechReady()) return;
        Platform.BarkHead.Engine?.DispatchIdle();
    }

    /// <summary>WPF ClearStaleSpeechLatch (Speech.cs:3070): the latch is up with nothing left to lower it.
    /// The Avalonia tube has no lead-in timer.</summary>
    private void ClearStaleSpeechLatch()
    {
        if (!ConditioningControlPanel.Services.Companion.SpeechLatchRule.IsLatchStale(
                isGiggling: _isGiggling, bubbleVisible: _speechBubble.IsVisible, waitingForAi: _isWaitingForAi,
                queueEmpty: _speechQueue.Count == 0, leadInPending: false, delayPending: _speechDelayPending))
            return;
        _isGiggling = false;
        _isMouseOverSpeechBubble = false;
        _lastSpeechEndTime = SpeechNow;
        Serilog.Log.Warning("[Speech] stale speech latch cleared - the companion was wedged silent");
    }
}
