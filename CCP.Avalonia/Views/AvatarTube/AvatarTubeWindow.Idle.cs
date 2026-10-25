using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private DispatcherTimer? _idleTimer;

    // WPF Speech.cs:1571-1634: the slider controls cadence, speech resets it.
    public void RestartIdleTimer()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RestartIdleTimer); return; }
        _idleTimer?.Stop();
        if (!IsVisible) return;
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds) };
        _idleTimer.Tick += (_, _) => IdleTick();
        _idleTimer.Start();
    }

    internal void IdleTick()
    {
        if (_idleTimer != null) _idleTimer.Interval = TimeSpan.FromSeconds(CoreSettings.Current.IdleGiggleIntervalSeconds);
        if (IsSpeechReady()) BarkHead.Engine?.DispatchIdle();
    }
}
