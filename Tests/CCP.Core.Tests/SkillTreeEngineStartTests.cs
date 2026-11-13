using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

public class SkillTreeEngineStartTests
{
    [Theory]
    [InlineData(23, 2, 0)]
    [InlineData(6, 0, 2)]
    [InlineData(12, 0, 0)]
    public void CountsBothWpfCalls(int hour, int night, int morning)
    {
        var s = new AppSettings();
        SkillTreeEngineStart.Apply(s, new DateTime(2026, 1, 8, hour, 30, 0), new DateTime(2026, 1, 8));
        Assert.Equal(night, s.NightTimeUsageCount);
        Assert.Equal(morning, s.EarlyMorningUsageCount);
    }

    [Theory]
    [InlineData(true, 7, true)]
    [InlineData(true, 8, true)]
    [InlineData(true, 6, false)]
    [InlineData(false, 8, false)]
    public void RefillsOnlyOwnedExpiredShield(bool owned, int days, bool refill)
    {
        var today = new DateTime(2026, 1, 10);
        var s = new AppSettings { LastStreakShieldResetDate = today.AddDays(-days), StreakShieldsRemaining = 0 };
        if (owned) s.UnlockedSkills.Add("good_girl_streak");
        SkillTreeEngineStart.Apply(s, today.AddHours(12), today);
        Assert.Equal(refill ? 1 : 0, s.StreakShieldsRemaining);
        Assert.Equal(refill ? today : today.AddDays(-days), s.LastStreakShieldResetDate);
    }
}
