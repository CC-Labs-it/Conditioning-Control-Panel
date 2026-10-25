using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private DispatcherTimer? _idleTimer;
    internal TimeSpan IdleInterval => _idleTimer?.Interval ?? TimeSpan.Zero;

    private void ResetIdleTimer()
    {
        RestartIdleTimer();
        QuickShell?.Autonomy.ReportUserActivity();
    }

    // WPF Speech.cs:1534-1591. A restart starts a full interval, never an immediate bark.
    public void RestartIdleTimer()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RestartIdleTimer); return; }
        _idleTimer?.Stop();
        _idleTimer ??= new DispatcherTimer();
        _idleTimer.Tick -= OnIdleTick;
        _idleTimer.Tick += OnIdleTick;
        _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        _idleTimer.Start();
    }

    internal void OnIdleTick(object? sender, EventArgs e)
    {
        var configured = CoreSettings.Current.IdleGiggleIntervalSeconds;
        if (_idleTimer != null && Math.Abs(_idleTimer.Interval.TotalSeconds - configured) > 0.5)
            _idleTimer.Interval = TimeSpan.FromSeconds(configured);
        if (global::ConditioningControlPanel.Services.Companion.SpeechLatchRule.IsLatchStale(
            _isGiggling, _speechBubble.IsVisible, _isWaitingForAi, _speechQueue.Count == 0,
            leadInPending: false, delayPending: _speechDelayPending))
        {
            _isGiggling = false;
            _isMouseOverSpeechBubble = false;
            _lastSpeechEndTime = SpeechNow;
        }
        if (!IsSpeechReady()) return;
        if (BarkHead.Engine is { } bark) bark.DispatchIdle();
        else Giggle(RandomBambiPhrase());
    }
}
