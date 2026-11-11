using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class SeasonActivityTests
{
    [Fact]
    public void MarksUniqueUtcDaysAndOpensBucket()
    {
        var s = new AppSettings();
        var now = new DateTime(2026, 7, 3, 23, 59, 0, DateTimeKind.Utc);
        Assert.True(SeasonActivity.MarkActiveToday(s, now));
        Assert.False(SeasonActivity.MarkActiveToday(s, now));
        Assert.True(SeasonActivity.MarkActiveToday(s, now.AddMinutes(2)));
        Assert.Equal("2026-07", s.SeasonStatsSeason);
        Assert.Equal(new[] { "2026-07-03", "2026-07-04" }, s.SeasonActiveDays);
    }

    [Fact]
    public void PeakSurvivesReset()
    {
        var s = new AppSettings();
        SeasonActivity.TrackStreakPeak(s, 12);
        SeasonActivity.TrackStreakPeak(s, 1);
        Assert.Equal(12, s.SeasonPeakStreak);
        Assert.False(string.IsNullOrEmpty(s.SeasonStatsSeason));
    }
}
