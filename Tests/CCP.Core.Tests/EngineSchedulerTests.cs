using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF MainWindow.StartStop.cs:834-932 scheduler rules.
[Collection(SessionStatics.Name)]
public sealed class EngineSchedulerTests
{
    private static AppSettings Schedule(string start, string end, bool today = true)
    {
        var s = new AppSettings
        {
            SchedulerEnabled = true, SchedulerStartTime = start, SchedulerEndTime = end,
            SchedulerMonday = today, SchedulerTuesday = today, SchedulerWednesday = today, SchedulerThursday = today,
            SchedulerFriday = today, SchedulerSaturday = today, SchedulerSunday = today,
        };
        return s;
    }

    private static readonly DateTime Day = new(2026, 10, 22);   // a Thursday

    [Theory]
    [InlineData("16:00", "22:00", 16, 0, true)]    // start inclusive
    [InlineData("16:00", "22:00", 22, 0, false)]   // end exclusive
    [InlineData("16:00", "22:00", 15, 59, false)]
    [InlineData("22:00", "02:00", 23, 30, true)]   // overnight
    [InlineData("22:00", "02:00", 1, 59, true)]
    [InlineData("22:00", "02:00", 12, 0, false)]
    [InlineData("junk", "junk", 17, 0, true)]      // falls back to 16:00-22:00
    public void Window_matches_WPF_IsInScheduledTimeWindow(string start, string end, int h, int m, bool expected) =>
        Assert.Equal(expected, CoreScheduler.InWindow(Schedule(start, end), Day.AddHours(h).AddMinutes(m)));

    [Fact]
    public void Window_is_closed_on_an_inactive_day() =>
        Assert.False(CoreScheduler.InWindow(Schedule("00:00", "23:59", today: false), Day.AddHours(12)));

    [Fact]
    public void Tick_starts_once_holds_off_after_a_manual_stop_and_stops_only_what_it_started()
    {
        var s = Schedule("16:00", "22:00");
        var sch = new CoreScheduler();
        var inside = Day.AddHours(17);
        var outside = Day.AddHours(23);

        Assert.Equal(CoreScheduler.Step.Start, sch.OnTick(s, inside, running: false));
        Assert.Equal(CoreScheduler.Step.None, sch.OnTick(s, inside, running: true));
        sch.OnManualToggle(s, inside, stopping: true);   // the user's own Stop inside the window
        Assert.Equal(CoreScheduler.Step.None, sch.OnTick(s, inside, running: false));
        Assert.Equal(CoreScheduler.Step.Stop, sch.OnTick(s, outside, running: true));   // still auto-started
        Assert.Equal(CoreScheduler.Step.None, sch.OnTick(s, outside, running: false));   // resets both flags
        Assert.False(sch.ManuallyStopped || sch.AutoStarted);
        Assert.Equal(CoreScheduler.Step.Start, sch.OnTick(s, inside.AddDays(1), running: false));

        var manual = new CoreScheduler();   // a manual start is never auto-stopped
        Assert.Equal(CoreScheduler.Step.None, manual.OnTick(s, outside, running: true));
        manual.OnManualToggle(s, inside, stopping: true);   // the user started by hand, then stopped in the window
        Assert.Equal(CoreScheduler.Step.None, manual.OnTick(s, inside, running: false));
        manual.OnManualToggle(s, inside, stopping: false);
        Assert.Equal(CoreScheduler.Step.Start, manual.OnTick(s, inside, running: false));
        s.SchedulerEnabled = false;
        Assert.Equal(CoreScheduler.Step.None, new CoreScheduler().OnTick(s, inside, running: false));
        Assert.Equal(CoreScheduler.Step.None, new CoreScheduler().OnStartup(s, inside));
    }

}
