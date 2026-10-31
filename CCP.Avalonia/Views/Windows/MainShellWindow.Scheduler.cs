// PORTED from ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs: the Scheduler region
// (:794-932, rules in Core CoreScheduler), the scheduler timer + 60 s grace from MainWindow.xaml.cs:606-629;
// CheckSchedulerAfterSettingsChange is dead in WPF (MainWindow.Settings.cs:357), so it is not ported.

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

        /// <summary>WPF MainWindow ctor: a 30 s check that starts after a 60 s grace (update restarts).</summary>
        private void InitializeScheduler()
        {
            Opened += (_, _) =>
            {
                // Opened fires on every Show (tray and back): arm once, or the first grace timer leaks the window.
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

        /// <summary>WPF CheckSchedulerOnStartup.</summary>
        internal void SchedulerStartupCheck(DateTime now)
        {
            if (Scheduler.OnStartup(CoreSettings.Current, now) != CoreScheduler.Step.Start) return;
            Serilog.Log.Information("Scheduler: App started within scheduled time window - auto-starting");
            MinimizeToTray();
            Platform.OsNotifications.Show(Loc.Get("notif_scheduler_active"), Loc.Get("notif_scheduler_autostarted"));
            StartEngine();
        }

        /// <summary>WPF SchedulerTimer_Tick.</summary>
        internal void SchedulerTick(DateTime now)
        {
            // Cancel a portal-delayed scheduled start if its window ended while waiting.
            if (Scheduler.AutoStarted && !CoreEngine.IsRunning && !CoreScheduler.InWindow(CoreSettings.Current, now))
                _engineGen++;
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

        /// <summary>WPF TrayIconService.MinimizeToTray: hide to the tray (first time with the balloon).
        /// No tray host on this desktop: the window stays, as closing it would.</summary>
        private void MinimizeToTray()
        {
            if (Tray is null || !TrayHostPresent()) return;
            Hide();
            HideAvatarTube();
            if (_shownFirstMinimizeNotification) return;
            _shownFirstMinimizeNotification = true;
            Platform.OsNotifications.Show(Loc.Get("app_title"), Loc.Get("tray_balloon_body"), ShowFromTray);
        }

    }
}
