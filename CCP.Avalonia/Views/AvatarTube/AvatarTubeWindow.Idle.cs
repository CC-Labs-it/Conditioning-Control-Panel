using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private DispatcherTimer? _idleTimer;

    // WPF AvatarTube/AvatarTubeWindow.Speech.cs:1571-1624: the slider owns cadence, the bark engine owns selection.
    public void RestartIdleTimer()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RestartIdleTimer);
            return;
        }
        _idleTimer?.Stop();
        _idleTimer ??= CreateIdleTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        if (IsVisible) _idleTimer.Start();
    }

    private DispatcherTimer CreateIdleTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => OnIdleTick();
        return timer;
    }

    internal bool IdleTimerRunning => _idleTimer?.IsEnabled == true;
    internal TimeSpan? IdleInterval => _idleTimer?.Interval;

    internal void OnIdleTick()
    {
        if (_idleTimer != null)
            _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        ClearStaleSpeechLatch();
        if (IdleTimerRunning && IsVisible && IsSpeechReady()) BarkHead.Engine?.DispatchIdle();
    }
    internal void ClearStaleSpeechLatch()
    {
        if (ConditioningControlPanel.Services.Companion.SpeechLatchRule.IsLatchStale(
            _isGiggling, _speechBubble.IsVisible, _isWaitingForAi, _speechQueue.Count == 0,
            leadInPending: false, delayPending: _speechDelayPending))
            _isGiggling = false; // this head shows speech synchronously; it has no lead-in timer
    }
}
