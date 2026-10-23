// Adapted from parity/trunk a4ee52d40cd347d288dcf7fdaa8445b79f8e5038.
// WPF MainWindow.StartStop.cs ramp + scheduler; UI timers use the existing Core rules.
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal readonly IntensityRampRun Ramp = new();
    internal readonly SchedulerRun Scheduler = new();
    internal Func<DateTime> EngineNow = () => DateTime.Now;
    private DispatcherTimer? _rampTimer, _schedulerTimer;
    private IDisposable? _schedulerGrace;

    internal void StartEngineLifecycle()
    {
        var s = CoreSettings.Current;
        if (s.AudioOnlySession) Platform.LayeredAudio.Instance?.Start(ignoreMasterToggle: true);
        int hour = EngineNow().Hour;
        if (hour >= 23 || hour < 5) s.NightTimeUsageCount++;
        if (hour >= 5 && hour < 8) s.EarlyMorningUsageCount++;
        CoreSettings.Save(); // WPF SkillTreeService.TrackTimeOfDayUsage
        EngineCrashSentinel.Mark(
            $"started {EngineNow():yyyy-MM-dd HH:mm:ss} | flash {s.FlashFrequency}/min x{s.SimultaneousImages}" +
            $" | bubbles {(s.BubblesEnabled ? "on" : "off")} | video {(s.MandatoryVideosEnabled ? "on" : "off")}" +
            $" | subliminal {(s.SubliminalEnabled ? "on" : "off")}");
        if (AutonomyScheduler.HasEntitlement && s.AutonomyModeEnabled && s.AutonomyConsentGiven) Autonomy.Start();
        if (!s.IntensityRampEnabled) return;
        StopRampTimer();
        Ramp.Start(s, EngineNow());
        _rampTimer = new DispatcherTimer { Interval = IntensityRampRun.TickInterval };
        _rampTimer.Tick += (_, _) => TickEngineRamp();
        _rampTimer.Start();
    }

    internal void StopRampTimer()
    {
        _rampTimer?.Stop();
        _rampTimer = null;
        Ramp.Stop(CoreSettings.Current);
    }

    internal void TickEngineRamp()
    {
        if (!Ramp.Tick(CoreSettings.Current, EngineNow(), App.Sessions?.IsRunning == true)) return;
        Platform.OsNotifications.Show("Session Complete", "Intensity ramp finished. Stopping...");
        StopEngine();
    }

    // Desktop startup only: renders/tests do not arm an automatic engine start.
    internal void StartSchedulerClock()
    {
        if (_schedulerGrace != null || _schedulerTimer != null) return;
        _schedulerGrace = DispatcherTimer.RunOnce(() =>
        {
            _schedulerTimer = new DispatcherTimer { Interval = SchedulerRun.PollInterval };
            _schedulerTimer.Tick += (_, _) => TickEngineScheduler();
            _schedulerTimer.Start();
            ApplySchedulerAction(Scheduler.CheckOnStartup(CoreSettings.Current, EngineNow()));
        }, SchedulerRun.GracePeriod);
        Closed += (_, _) => { _schedulerGrace?.Dispose(); _schedulerTimer?.Stop(); StopRampTimer(); };
    }

    internal void TickEngineScheduler() =>
        ApplySchedulerAction(Scheduler.Tick(CoreSettings.Current, EngineNow(), CoreEngine.IsRunning));

    private void ApplySchedulerAction(SchedulerRun.Action action)
    {
        if (action == SchedulerRun.Action.Start)
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
