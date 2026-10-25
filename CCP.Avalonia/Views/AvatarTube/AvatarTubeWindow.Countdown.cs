using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private DispatcherTimer? _takeoverCountdownTimer;
    private AutonomyScheduler? _countdownAutonomy;
    private readonly ScaleTransform _takeoverScale = new(1, 1);
    private Border? _countdownBar;
    internal bool CountdownTicking => _takeoverCountdownTimer?.IsEnabled == true;

    // WPF AvatarTubeWindow.Avatar.cs:43-126. No new window or animation loop: the existing tube
    // owns a 100 ms ticker only while Takeover is enabled, and detaches it on close.
    private void InitTakeoverCountdown()
    {
        if (_countdownAutonomy != null) return;
        this.FindControl<Border>("TakeoverCountdownFill")!.RenderTransform = _takeoverScale;
        _countdownAutonomy = (_parentWindow as Windows.MainShellWindow)?.Autonomy;
        if (_countdownAutonomy == null) return;
        _countdownAutonomy.EnabledChanged += OnCountdownEnabled;
        OnCountdownEnabled(null, _countdownAutonomy.IsEnabled);
    }

    private void OnCountdownEnabled(object? sender, bool enabled)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnCountdownEnabled(sender, enabled));
            return;
        }
        if (_countdownAutonomy == null) return; // closed while the post was queued
        if (enabled)
        {
            _takeoverCountdownTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100),
                DispatcherPriority.Render, (_, _) => UpdateTakeoverCountdown());
            _takeoverCountdownTimer.Start();
        }
        else _takeoverCountdownTimer?.Stop();
        UpdateTakeoverCountdown();
    }

    internal void UpdateTakeoverCountdown()
    {
        var fraction = CoreSettings.Current.ShowTakeoverCountdownBar
            ? _countdownAutonomy?.NextRandomFireFraction : null;
        (_countdownBar ??= this.FindControl<Border>("TakeoverCountdownBar")!).IsVisible = fraction.HasValue;
        _takeoverScale.ScaleX = fraction ?? 0;
    }

    private void StopTakeoverCountdown()
    {
        if (_countdownAutonomy != null) _countdownAutonomy.EnabledChanged -= OnCountdownEnabled;
        _countdownAutonomy = null;
        _takeoverCountdownTimer?.Stop();
    }
}
