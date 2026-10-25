using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private DispatcherTimer? _idleTimer;
    internal bool IdleTimerRunning => _idleTimer?.IsEnabled == true;

    // WPF Speech.cs:1571-1633. One timer per open tube, never per render/construction.
    public void RestartIdleTimer() => RunOnAvatar(() =>
    {
        StopIdleTimer();
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds) };
        _idleTimer.Tick += OnIdleTick;
        _idleTimer.Start();
    });

    private void StopIdleTimer()
    {
        if (_idleTimer == null) return;
        _idleTimer.Stop();
        _idleTimer.Tick -= OnIdleTick;
        _idleTimer = null;
    }

    private void OnIdleTick(object? sender, EventArgs e) => DispatchIdleTick();

    internal void DispatchIdleTick()
    {
        if (!IdleTimerRunning) return; // panic/close also rejects an already queued tick
        _idleTimer!.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        if (ConditioningControlPanel.Services.Companion.SpeechLatchRule.IsLatchStale(
            _isGiggling, _speechBubble.IsVisible, _isWaitingForAi, _speechQueue.Count == 0,
            false, _speechDelayPending)) _isGiggling = false;
        if (!IsSpeechReady()) return;
        if (BarkHead.Engine is { } engine) engine.DispatchIdle();
        else GigglePriority(RandomBambiPhrase(), playSound: false, aiGenerated: false);
        // ponytail: no-engine fallback is text-only until preset queued speech carries unprompted metadata.
    }
}
