using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class LoginStreakTests
{
    private static readonly DateTime Today = new(2026, 11, 12);

    [Fact]
    public void NewAndNextDayUpdatePeakOnce()
    {
        var p = new AchievementProgress(); var s = new AppSettings();
        LoginStreak.Advance(p, s, Today, Today);
        Assert.Equal(1, p.ConsecutiveDays); Assert.Equal(1, s.SeasonPeakStreak);
        LoginStreak.Advance(p, s, Today, Today);
        Assert.Equal(1, p.ConsecutiveDays);
        LoginStreak.Advance(p, s, Today.AddDays(1), Today.AddDays(1));
        Assert.Equal(2, p.ConsecutiveDays); Assert.Equal(2, s.SeasonPeakStreak);
        Assert.Equal(2, s.CurrentStreak); Assert.True(p.PendingStreakBonus);
    }

    [Fact]
    public void GapUsesShieldBeforeInsuranceAndRetainsPeak()
    {
        var p = new AchievementProgress { ConsecutiveDays = 7, LastLaunchDate = Today.AddDays(-3) };
        var s = new AppSettings { StreakShieldsRemaining = 1, StreakFixCharges = 1 };
        s.UnlockedSkills.Add("good_girl_streak"); s.UnlockedSkills.Add("oopsie_insurance");
        Assert.False(LoginStreak.Advance(p, s, Today, Today));
        Assert.Equal(8, p.ConsecutiveDays); Assert.Equal(8, s.SeasonPeakStreak);
        Assert.Equal(0, s.StreakShieldsRemaining); Assert.Equal(1, s.StreakFixCharges);
        Assert.Equal(2, s.StreakShieldUsedDates.Count);
        p.LastLaunchDate = Today.AddDays(-3);
        Assert.True(LoginStreak.Advance(p, s, Today, Today));
        Assert.Equal(8, p.ConsecutiveDays); Assert.Equal(0, s.StreakFixCharges);
        p.LastLaunchDate = Today.AddDays(-3);
        Assert.False(LoginStreak.Advance(p, s, Today, Today));
        Assert.Equal(1, p.ConsecutiveDays); Assert.Equal(8, s.SeasonPeakStreak);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 50)] [InlineData(4, 100)]
    [InlineData(7, 150)] [InlineData(14, 200)] [InlineData(30, 300)]
    public void BonusFollowsWpfTiers(int streak, int xp) => Assert.Equal(xp, LoginStreak.Bonus(streak));

    [Fact]
    public void FailedCloudAnswerResolvesThePendingGapOnce()
    {
        var p = new AchievementProgress { ConsecutiveDays = 5, LastLaunchDate = Today.AddDays(-4) };
        var s = new AppSettings { UnifiedId = "account", StreakShieldsRemaining = 1 };
        s.UnlockedSkills.Add("good_girl_streak");
        LoginStreak.Advance(p, s, Today, Today);
        LoginStreak.Advance(p, s, Today, Today, resolve: true);
        LoginStreak.Advance(p, s, Today, Today, resolve: true);
        Assert.Equal(6, p.ConsecutiveDays); Assert.Equal(6, s.SeasonPeakStreak);
        Assert.Equal(0, s.StreakShieldsRemaining); Assert.False(p.PendingStreakBreak);
    }

    [Fact]
    public void OldCloudRunThatDoesNotCoverTheGapDoesNotRaiseThePeakBeforeReset()
    {
        var p = new AchievementProgress { ConsecutiveDays = 5, LastLaunchDate = Today.AddDays(-4) };
        var s = new AppSettings { UnifiedId = "account" };
        LoginStreak.Advance(p, s, Today, Today);
        Assert.True(LoginStreak.Adopt(p, 8, Today.AddDays(-3), Today));
        LoginStreak.Advance(p, s, Today, Today, resolve: true);
        Assert.Equal(1, p.ConsecutiveDays);
        Assert.Equal(5, s.SeasonPeakStreak); // WPF tracks after deferred gap resolution.
    }

    [Fact]
    public void SignedInGapWaitsForCloudThenAdoptsAndBanksToday()
    {
        var p = new AchievementProgress { ConsecutiveDays = 5, LastLaunchDate = Today.AddDays(-4) };
        var s = new AppSettings { UnifiedId = "account", StreakShieldsRemaining = 1 };
        s.UnlockedSkills.Add("good_girl_streak");
        LoginStreak.Advance(p, s, Today, Today);
        Assert.True(p.PendingStreakBreak); Assert.Equal(1, s.StreakShieldsRemaining);
        Assert.Equal(Today.AddDays(-4), p.LastLaunchDate);
        Assert.True(LoginStreak.Adopt(p, 8, Today.AddDays(-1), Today));
        LoginStreak.Advance(p, s, Today, Today, resolve: true);
        Assert.False(p.PendingStreakBreak); Assert.Equal(9, p.ConsecutiveDays);
        Assert.Equal(9, s.SeasonPeakStreak); Assert.Equal(1, s.StreakShieldsRemaining);
    }
}
