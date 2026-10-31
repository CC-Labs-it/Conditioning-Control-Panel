using System;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal readonly CoreScheduler Scheduler = new();
        private DispatcherTimer? _schedulerTimer;
        private IDisposable? _schedulerGrace;
        private void InitializeScheduler()
        {
            Opened += (_, _) =>
            {
                if (_schedulerGrace != null) return;
                Serilog.Log.Information("Scheduler will start after {Seconds}s grace period", 60);
                _schedulerGrace = DispatcherTimer.RunOnce(() =>
                {
                    _schedulerTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Normal,
                        (_, _) => SchedulerTick(DateTime.Now));
                    _schedulerTimer.Start();
                    SchedulerStartupCheck(DateTime.Now);
                    Serilog.Log.Information("Scheduler grace period complete - scheduler now active");
                }, TimeSpan.FromSeconds(60));
            };
            Closed += (_, _) => { _schedulerGrace?.Dispose(); _schedulerTimer?.Stop(); };
        }
        internal void SchedulerStartupCheck(DateTime now)
        {
            if (Scheduler.OnStartup(CoreSettings.Current, now) != CoreScheduler.Step.Start) return;
            Serilog.Log.Information("Scheduler: App started within scheduled time window - auto-starting");
            MinimizeToTray();
            Platform.OsNotifications.Show(Loc.Get("notif_scheduler_active"), Loc.Get("notif_scheduler_autostarted"));
            StartEngine();
        }
        internal void SchedulerTick(DateTime now)
        {
            switch (Scheduler.OnTick(CoreSettings.Current, now, CoreEngine.IsRunning))
            {
                case CoreScheduler.Step.Start:
                    Serilog.Log.Information("Scheduler: Entering scheduled time window - auto-starting");
                    MinimizeToTray();
                    Platform.OsNotifications.Show(Loc.Get("notif_scheduler_active"), Loc.Get("notif_scheduler_autostarted"));
                    StartEngine();
                    break;
                case CoreScheduler.Step.Stop:
                    Serilog.Log.Information("Scheduler: Exiting scheduled time window - auto-stopping");
                    StopEngine();
                    Platform.OsNotifications.Show(Loc.Get("notif_scheduler"), Loc.Get("notif_scheduler_ended"));
                    break;
            }
        }
        private void MinimizeToTray()
        {
            if (Tray is null || !TrayHostPresent()) return;
            Hide();
            HideAvatarTube();
            if (_shownFirstMinimizeNotification) return;
            _shownFirstMinimizeNotification = true;
            Platform.OsNotifications.Show(Loc.Get("app_title"), Loc.Get("tray_balloon_body"), ShowFromTray);
        }
        internal void OnRampComplete()
        {
            Platform.OsNotifications.Show(Loc.Get("notif_ramp_complete"), Loc.Get("notif_ramp_complete_body"));
            StopEngine();
        }
    }
}
