using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private AutonomyScheduler? _countdownAutonomy;
    private DispatcherTimer? _takeoverCountdownTimer;
    internal bool CountdownTickRunning => _takeoverCountdownTimer?.IsEnabled == true;

    // WPF AvatarTubeWindow.Avatar.cs: 100 ms, only while Takeover runs; no render loop.
    private void InitTakeoverCountdownBar()
    {
        _countdownAutonomy = (_parentWindow as MainShellWindow)?.Autonomy;
        if (_countdownAutonomy == null) return;
        _countdownAutonomy.EnabledChanged += FollowTakeoverCountdown;
        FollowTakeoverCountdown(null, _countdownAutonomy.IsEnabled);
    }

    private void FollowTakeoverCountdown(object? sender, bool enabled)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => FollowTakeoverCountdown(sender, _countdownAutonomy?.IsEnabled == true));
            return;
        }
        if (enabled)
        {
            _takeoverCountdownTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100),
                DispatcherPriority.Render, (_, _) => RefreshTakeoverCountdown());
            _takeoverCountdownTimer.Start();
        }
        else _takeoverCountdownTimer?.Stop();
        RefreshTakeoverCountdown();
    }

    internal void RefreshTakeoverCountdown()
    {
        var fraction = CoreSettings.Current.ShowTakeoverCountdownBar
            ? _countdownAutonomy?.NextRandomFireFraction : null;
        this.FindControl<Border>("TakeoverCountdownBar")!.IsVisible = fraction != null;
        if (fraction is { } value)
            ((ScaleTransform)this.FindControl<Border>("TakeoverCountdownFill")!.RenderTransform!).ScaleX = value;
    }

    private void StopTakeoverCountdownBar()
    {
        if (_countdownAutonomy != null) _countdownAutonomy.EnabledChanged -= FollowTakeoverCountdown;
        _countdownAutonomy = null;
        _takeoverCountdownTimer?.Stop();
    }
}
