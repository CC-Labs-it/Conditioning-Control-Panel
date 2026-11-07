using System;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PerfectWeekQuestTests
{
    [Theory]
    [InlineData(7, 3060)]
    [InlineData(14, 6120)]
    [InlineData(21, 3060)]
    [InlineData(28, 3060)]
    [InlineData(30, 10200)]
    [InlineData(31, 0)]
    public void MilestonesScaleAndPayOnceAcrossDailySeatsAndRelaunch(int streak, int expected)
    {
        var s = Owned(streak);
        int paid = 0, saves = 0;
        Assert.Equal(expected, SkillTreeRules.ClaimPerfectWeekBonus(s, xp =>
        {
            Assert.NotEqual(streak, s.LastPerfectWeekStreakAwarded);
            paid += xp;
        }, () => saves++));
        Assert.Equal(expected, paid);
        Assert.Equal(expected > 0 ? 1 : 0, saves);
        var relaunched = Owned(streak);
        relaunched.LastPerfectWeekStreakAwarded = s.LastPerfectWeekStreakAwarded;
        Assert.Equal(0, SkillTreeRules.ClaimPerfectWeekBonus(relaunched, _ => Assert.Fail("Duplicate grant"), () => saves++));
    }

    [Fact]
    public void BrokenStreakResetsLatchImmediatelyAndCanEarnAgain()
    {
        var s = Owned(2);
        s.LastPerfectWeekStreakAwarded = 14;
        int saves = 0;
        Assert.Equal(0, SkillTreeRules.ClaimPerfectWeekBonus(s, _ => Assert.Fail("Not a milestone"), () => saves++));
        Assert.Equal(0, s.LastPerfectWeekStreakAwarded);
        Assert.Equal(1, saves);
        s.DailyQuestStreak = 7;
        Assert.Equal(3060, SkillTreeRules.ClaimPerfectWeekBonus(s, _ => { }, () => saves++));
    }

    [Fact]
    public void RequiresSkillAndFailedGrantDoesNotBurnMilestone()
    {
        var s = Owned(7);
        s.UnlockedSkills.Clear();
        Assert.Equal(0, SkillTreeRules.ClaimPerfectWeekBonus(s, _ => Assert.Fail("Unowned"), () => Assert.Fail("Unowned")));
        s.UnlockedSkills.Add("perfect_bimbo_week");
        Assert.Throws<InvalidOperationException>(() => SkillTreeRules.ClaimPerfectWeekBonus(s,
            _ => throw new InvalidOperationException(), () => Assert.Fail("Must grant first")));
        Assert.Equal(0, s.LastPerfectWeekStreakAwarded);
    }

    private static AppSettings Owned(int streak)
    {
        var s = new AppSettings { PlayerLevel = 1, DailyQuestStreak = streak };
        s.UnlockedSkills.Add("perfect_bimbo_week");
        return s;
    }
}
