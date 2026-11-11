using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class SeasonActivityTests
{
    [Fact]
    public void MarkIsIdempotentAndOpensBucket()
    {
        var s = new AppSettings();
        var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var saves = 0;
        SeasonActivity.MarkActiveToday(s, now, () => saves++);
        SeasonActivity.MarkActiveToday(s, now.AddHours(1), () => saves++);
        Assert.Equal("2026-11", s.SeasonStatsSeason);
        Assert.Equal(new[] { "2026-11-11" }, s.SeasonActiveDays);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void PeakOpensBucketAndSurvivesLowerStreak()
    {
        var s = new AppSettings();
        var now = new DateTime(2026, 11, 11);
        SeasonActivity.TrackStreakPeak(s, 7, now);
        SeasonActivity.TrackStreakPeak(s, 2, now);
        Assert.Equal("2026-11", s.SeasonStatsSeason);
        Assert.Equal(7, s.SeasonPeakStreak);
    }
}
