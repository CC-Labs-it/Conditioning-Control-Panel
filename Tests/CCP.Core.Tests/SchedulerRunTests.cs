using System;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public class SchedulerRunTests
{
    private static readonly DateTime Monday = new(2026, 10, 26, 17, 0, 0);
    private static AppSettings Settings() => new() { SchedulerEnabled = true,
        SchedulerMonday = true, SchedulerTuesday = true, SchedulerStartTime = "16", SchedulerEndTime = "22" };

    [Fact]
    public void PanicBeforeFirstTickOrGraceCallbackBlocksStartUntilWindowExit()
    {
        var s = Settings();
        var run = new SchedulerRun();
        // No AutoStarted flag to mask a missing manual-stop check.
        run.NoteManualStop(s, Monday);
        Assert.True(run.ManuallyStoppedDuringSchedule);
        Assert.Equal(SchedulerRun.Action.None, run.CheckOnStartup(s, Monday));
        Assert.Equal(SchedulerRun.Action.None, run.Tick(s, Monday, false));
        Assert.Equal(SchedulerRun.Action.None, run.Tick(s, Monday.Date.AddHours(22), false));
        Assert.False(run.ManuallyStoppedDuringSchedule);
        Assert.Equal(SchedulerRun.Action.Start, run.Tick(s, Monday.AddDays(1), false));
    }

    [Fact]
    public void AutomaticRunsStopAtEndButManualRunsDoNot()
    {
        var s = Settings();
        var run = new SchedulerRun();
        Assert.Equal(SchedulerRun.Action.Start, run.CheckOnStartup(s, Monday));
        Assert.Equal(SchedulerRun.Action.None, run.Tick(s, Monday, true));
        Assert.Equal(SchedulerRun.Action.Stop, run.Tick(s, Monday.Date.AddHours(22), true));
        Assert.Equal(SchedulerRun.Action.None, new SchedulerRun().Tick(s, Monday.Date.AddHours(22), true));
        run.NoteManualStop(s, Monday);
        run.NoteManualStart();
        Assert.False(run.ManuallyStoppedDuringSchedule);
    }

    [Fact]
    public void OvernightUsesTodaysDayAndEndIsExclusiveWithWpfFallbacks()
    {
        var s = Settings();
        s.SchedulerStartTime = "22:0"; s.SchedulerEndTime = "2.5";
        Assert.True(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(1)));
        Assert.False(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(2).AddMinutes(5)));
        s.SchedulerMonday = false;
        Assert.False(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(23)));
        s.SchedulerMonday = true; s.SchedulerStartTime = s.SchedulerEndTime = "bad";
        Assert.True(SchedulerRun.IsInWindow(s, Monday));
        Assert.False(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(22)));
        s.SchedulerEnabled = false;
        Assert.Equal(SchedulerRun.Action.None, new SchedulerRun().CheckOnStartup(s, Monday));
        Assert.Equal(SchedulerRun.Action.None, new SchedulerRun().Tick(s, Monday, false));
        Assert.Equal(TimeSpan.FromSeconds(30), SchedulerRun.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), SchedulerRun.GracePeriod);
    }
}
