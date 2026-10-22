using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private DispatcherTimer? _idleTimer;
    internal bool IdleTimerRunning => _idleTimer?.IsEnabled == true;
    internal TimeSpan IdleInterval => _idleTimer?.Interval ?? TimeSpan.Zero;

    /// <summary>WPF Speech.cs:1573-1624: the open tube owns the companion slider's idle cadence.</summary>
    public void RestartIdleTimer() => RunOnAvatar(() =>
    {
        if (!IsVisible) return;
        if (_idleTimer == null)
        {
            _idleTimer = new DispatcherTimer();
            _idleTimer.Tick += (_, _) => TickIdle();
        }
        _idleTimer.Stop();
        _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        _idleTimer.Start();
    });

    internal void TickIdle()
    {
        if (!IsVisible) return;
        var configured = CoreSettings.Current.IdleGiggleIntervalSeconds;
        if (_idleTimer != null && Math.Abs(_idleTimer.Interval.TotalSeconds - configured) > 0.5)
            _idleTimer.Interval = TimeSpan.FromSeconds(configured);
        if (!IsSpeechReady()) return;
        if (BarkHead.Engine is { } engine) engine.DispatchIdle();
        else Giggle(RandomBambiPhrase(), unprompted: true);
    }
}
