// WPF MainWindow.StartStop.cs scheduler/ramp host: 60 s grace, 30 s schedule, 2 s ramp.
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private readonly ShellSchedule _schedule = new();
    private readonly ShellRamp _shellRamp = new();
    private DispatcherTimer? _scheduleTimer, _rampTimer;
    private DateTime _scheduleReady;
    private void InitializeSchedule()
    {
        ShellRamp.HealOnLaunch(CoreSettings.Current);
        _scheduleReady = DateTime.Now.AddSeconds(60);
        _scheduleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _scheduleTimer.Tick += (_, _) =>
        {
            if (DateTime.Now < _scheduleReady) return;
            _scheduleTimer.Interval = TimeSpan.FromSeconds(30);
            ScheduleTick(DateTime.Now);
        };
        _scheduleTimer.Start();
        Closed += (_, _) => { _scheduleTimer.Stop(); StopShellRamp(); };
    }
    internal void ScheduleTick(DateTime now)
    {
        switch (_schedule.Tick(CoreSettings.Current, now, CoreEngine.IsRunning))
        {
            case 1:
                // Use the existing tray rather than Closing (Closing tears down Chaos).
                if (Tray != null)
                {
                    Tray.IsVisible = true; Hide(); HideAvatarTube();
                    if (!_shownFirstMinimizeNotification)
                    {
                        _shownFirstMinimizeNotification = true;
                        OsNotifications.Show(global::ConditioningControlPanel.Localization.Loc.Get("app_title"), global::ConditioningControlPanel.Localization.Loc.Get("tray_balloon_body"), ShowFromTray);
                    }
                }
                OsNotifications.Show("Scheduler Active", "Session auto-started based on schedule.");
                StartEngine();
                break;
            case -1:
                StopEngine();
                OsNotifications.Show("Scheduler", "Scheduled session ended.");
                break;
        }
    }
    private void StartShellRamp()
    {
        if (!CoreSettings.Current.IntensityRampEnabled) return;
        _shellRamp.Start(CoreSettings.Current, DateTime.Now);
        _rampTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _rampTimer.Tick += (_, _) => ShellRampTick(DateTime.Now);
        _rampTimer.Start();
    }
    internal void ShellRampTick(DateTime now)
    {
        if (!_shellRamp.Tick(CoreSettings.Current, now, App.Sessions?.IsRunning == true)) return;
        OsNotifications.Show("Session Complete", "Intensity ramp finished. Stopping...");
        StopEngine();
    }
    private void StopShellRamp()
    {
        _rampTimer?.Stop();
        _rampTimer = null;
        _shellRamp.Stop(CoreSettings.Current);
    }
}
