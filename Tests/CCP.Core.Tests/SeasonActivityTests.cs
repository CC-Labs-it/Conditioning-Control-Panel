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

    [Fact]
    public void FreshLaunchAndNextDayBankOnceAndRaisePeak()
    {
        var s = new AppSettings();
        var p = new AchievementProgress(); var moments = new System.Collections.Generic.List<(string, int)>(); Action<string, int> fire = (name, days) => moments.Add((name, days));
        var day = new DateTime(2026, 7, 3);
        Assert.True(SeasonActivity.UpdateStreak(s, p, day, false, moment: fire));
        Assert.Equal(1, p.ConsecutiveDays);
        Assert.False(SeasonActivity.UpdateStreak(s, p, day, false, moment: fire));
        Assert.True(SeasonActivity.UpdateStreak(s, p, day.AddDays(1), false, moment: fire));
        Assert.Equal(2, p.ConsecutiveDays);
        Assert.Equal(2, s.CurrentStreak);
        Assert.Equal(2, s.SeasonPeakStreak);
        Assert.True(p.PendingStreakBonus); Assert.Equal(new[] { ("streakKept", 2) }, moments);
        Assert.False(SeasonActivity.UpdateStreak(s, p, day.AddDays(1), false, moment: fire));
    }

    [Fact]
    public void DeferredCloudCoveredGapDoesNotSpendShieldAndBanksToday()
    {
        var day = new DateTime(2026, 7, 8);
        var s = new AppSettings { UnlockedSkills = new() { "good_girl_streak" }, StreakShieldsRemaining = 1 };
        var p = new AchievementProgress { ConsecutiveDays = 4, LastLaunchDate = day.AddDays(-4) }; var moments = new System.Collections.Generic.List<(string, int)>(); Action<string, int> fire = (name, days) => moments.Add((name, days));
        Assert.False(SeasonActivity.UpdateStreak(s, p, day, true, moment: fire));
        Assert.True(p.PendingStreakBreak);
        Assert.Equal(day.AddDays(-4), p.LastLaunchDate);
        Assert.True(SeasonActivity.AdoptStreak(s, p, Newtonsoft.Json.Linq.JObject.Parse(
            "{\"consecutive_days\":7,\"last_streak_date\":\"2026-07-07\"}"), day));
        Assert.True(SeasonActivity.UpdateStreak(s, p, day, false, resolve: true, moment: fire));
        Assert.False(p.PendingStreakBreak);
        Assert.Equal(8, p.ConsecutiveDays);
        Assert.Equal(8, s.SeasonPeakStreak);
        Assert.Equal(1, s.StreakShieldsRemaining); Assert.Empty(moments);
        Assert.Equal(day, p.LastLaunchDate);
        Assert.False(SeasonActivity.UpdateStreak(s, p, day, false, resolve: true, moment: fire));
    }

    [Theory]
    [InlineData("good_girl_streak", 5, 0, 1)]
    [InlineData("oopsie_insurance", 4, 1, 0)]
    [InlineData("none", 1, 1, 1)]
    public void UncoveredGapUsesProtectionInOrderOrResets(string skill, int expected, int shields, int credits)
    {
        var day = new DateTime(2026, 7, 8);
        var s = new AppSettings { UnlockedSkills = new() { skill }, StreakShieldsRemaining = 1, StreakFixCharges = 1 };
        var p = new AchievementProgress { ConsecutiveDays = 4, LastLaunchDate = day.AddDays(-3) }; var moments = new System.Collections.Generic.List<(string, int)>(); Action<string, int> fire = (name, days) => moments.Add((name, days));
        Assert.False(SeasonActivity.UpdateStreak(s, p, day, true, moment: fire));
        Assert.True(SeasonActivity.UpdateStreak(s, p, day, false, resolve: true, moment: fire));
        Assert.Equal(expected, p.ConsecutiveDays); Assert.Equal(new[] { (skill == "none" ? "streakBroken" : "streakKept", skill == "good_girl_streak" ? 5 : 4) }, moments);
        Assert.Equal(shields, s.StreakShieldsRemaining);
        Assert.Equal(credits, s.StreakFixCharges);
        Assert.Equal(Math.Max(4, expected), s.SeasonPeakStreak);
        Assert.Equal(day, p.LastLaunchDate);
        if (skill == "good_girl_streak") Assert.Equal(new[] { day.AddDays(-2), day.AddDays(-1) }, s.StreakShieldUsedDates);
    }

    [Fact]
    public void StartupBeforeSkillTreeDoesNotSpendProtection()
    {
        var day = new DateTime(2026, 7, 8);
        var s = new AppSettings { UnlockedSkills = new() { "good_girl_streak" }, StreakShieldsRemaining = 1 };
        var p = new AchievementProgress { ConsecutiveDays = 4, LastLaunchDate = day.AddDays(-3) };
        Assert.True(SeasonActivity.UpdateStreak(s, p, day, false, allowProtection: false));
        Assert.Equal(1, p.ConsecutiveDays);
        Assert.Equal(1, s.StreakShieldsRemaining);
        Assert.Equal(4, s.SeasonPeakStreak);
    }

    [Fact]
    public void CloudIsoDateUsesWrittenDayAndRejectsFarFuture()
    {
        var today = new DateTime(2026, 7, 8);
        var s = new AppSettings();
        var p = new AchievementProgress { ConsecutiveDays = 2, LastLaunchDate = today.AddDays(-4) };
        var stats = new Newtonsoft.Json.Linq.JObject { ["consecutive_days"] = 5, ["last_streak_date"] = "2026-07-07T23:00:00-07:00" };
        Assert.True(SeasonActivity.AdoptStreak(s, p, stats, today));
        Assert.Equal(today.AddDays(-1), p.LastLaunchDate);
        stats["last_streak_date"] = "2030-01-01";
        stats["consecutive_days"] = 6;
        Assert.True(SeasonActivity.AdoptStreak(s, p, stats, today));
        Assert.Equal(today.AddDays(-1), p.LastLaunchDate);
    }
}
