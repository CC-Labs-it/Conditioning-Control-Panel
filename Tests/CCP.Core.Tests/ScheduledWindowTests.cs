using System;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public class ScheduledWindowTests
{
    [Fact]
    public void SameDayIncludesStartAndExcludesEnd()
    {
        var s = new AppSettings { SchedulerStartTime = "16:00", SchedulerEndTime = "22:00" };
        Assert.False(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 15, 59, 0)));
        Assert.True(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 16, 0, 0)));
        Assert.False(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 22, 0, 0)));
        s.SchedulerMonday = false;
        Assert.False(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 17, 0, 0)));
    }

    [Fact]
    public void OvernightUsesTodayAndEndIsExclusive()
    {
        var s = new AppSettings { SchedulerStartTime = "22", SchedulerEndTime = "2.5", SchedulerMonday = false };
        Assert.True(SchedulerTime.InWindow(s, new DateTime(2026, 11, 3, 2, 4, 0)));
        Assert.False(SchedulerTime.InWindow(s, new DateTime(2026, 11, 3, 2, 5, 0)));
        Assert.False(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 23, 0, 0)));
        Assert.True(SchedulerTime.InWindow(s, new DateTime(2026, 11, 3, 22, 0, 0)));
    }

    [Fact]
    public void InvalidTimesUseWpfDefaultsAndEqualTimesAreEmpty()
    {
        var s = new AppSettings { SchedulerStartTime = "bad", SchedulerEndTime = "bad" };
        Assert.True(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 16, 0, 0)));
        Assert.False(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 22, 0, 0)));
        s.SchedulerStartTime = s.SchedulerEndTime = "12";
        Assert.False(SchedulerTime.InWindow(s, new DateTime(2026, 11, 2, 12, 0, 0)));
    }
}
