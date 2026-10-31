using System;
using Avalonia.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal readonly SchedulerRun Scheduler = new();
    internal Func<DateTime> SchedulerNow = () => DateTime.Now;
    private DispatcherTimer? _schedulerTimer;
    private IDisposable? _schedulerGrace;

    // WPF MainWindow.xaml.cs:606-627. Desktop startup only, never renders or tests.
    internal void StartSchedulerClock()
    {
        if (_schedulerGrace != null || _schedulerTimer != null) return;
        _schedulerGrace = DispatcherTimer.RunOnce(() =>
        {
            _schedulerTimer = new DispatcherTimer { Interval = SchedulerRun.PollInterval };
            _schedulerTimer.Tick += (_, _) => TickEngineScheduler();
            _schedulerTimer.Start();
            // A panic during the grace period must win over this delayed start too.
            if (!PanicAftermath.HoldsEngineRestart)
                ApplySchedulerAction(Scheduler.CheckOnStartup(CoreSettings.Current, SchedulerNow()));
        }, SchedulerRun.GracePeriod);
        Closed += (_, _) => { _schedulerGrace?.Dispose(); _schedulerTimer?.Stop(); };
    }

    internal void TickEngineScheduler()
    {
        // Keep processing window exits during the hold, but never arm an automatic start.
        if (PanicAftermath.HoldsEngineRestart)
            Scheduler.NoteManualStop(CoreSettings.Current, SchedulerNow());
        ApplySchedulerAction(Scheduler.Tick(CoreSettings.Current, SchedulerNow(), CoreEngine.IsRunning));
    }

    private void ApplySchedulerAction(SchedulerRun.Action action)
    {
        // WPF MainWindow.StartStop.cs:795-874, with the base's indefinite panic hold retained.
        if (action == SchedulerRun.Action.Start && !PanicAftermath.HoldsEngineRestart)
        {
            if (Tray != null && TrayHostPresent()) { Hide(); HideAvatarTube(); }
            Platform.OsNotifications.Show("Scheduler Active", "Session auto-started based on schedule.");
            if (!CoreEngine.IsRunning) StartEngine();
        }
        else if (action == SchedulerRun.Action.Stop)
        {
            StopEngine();
            Platform.OsNotifications.Show("Scheduler", "Scheduled session ended.");
        }
    }
}
