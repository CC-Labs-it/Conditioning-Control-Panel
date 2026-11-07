using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF ProgressionService.SpendXPOnLevels:288 -> SkillTreeService.OnLevelUp:627-648: every level gained
/// credits one Sparkle, lifts the season peak, counts toward the lifetime earned stat and, after the save, raises
/// the wallet's reward cue. Web XP claims share the level loop, so their levels pay too (WPF AddClaimedXP).</summary>
[Collection(SessionStatics.Name)]
public sealed class LevelUpSparklesTests
{
    private static SettingsService Fresh(int level, int points)
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.OfflineMode = true;
        s.OfflineUsername = "sparkles-core-test";
        s.PlayerXP = 0;
        s.PlayerLevel = level;
        s.SkillPoints = points;
        s.SeasonPeakLevel = 0;
        s.DescentMigrationCompleted = true;
        return service;
    }

    [Fact]
    public void EachLevelGainedPaysOneSparkleAndCuesTheWallet()
    {
        var previous = CoreSettings.ServiceProvider;
        var service = Fresh(10, 3);
        var s = CoreSettings.Current;
        var awards = new List<SparklePointAward>();
        var earned = 0;
        EventHandler<SparklePointAward> cue = (_, a) => awards.Add(a);
        Action<int> stat = n => earned += n;
        SparklePointRewards.Awarded += cue;
        ProgressionBank.SkillPointsAwarded += stat;
        try
        {
            var epoch = XpCurve.EpochOf(s);
            var factor = ConditioningControlPanel.Services.Descent.DescentCycleXp.XpBonusFor(s);
            ProgressionBank.Add((XpCurve.GetXPForLevel(10, epoch) + XpCurve.GetXPForLevel(11, epoch)) / factor + 1, "Quest");

            Assert.Equal(12, s.PlayerLevel);
            Assert.Equal(5, s.SkillPoints);
            Assert.Equal(12, s.SeasonPeakLevel);
            Assert.Equal(2, earned);
            Assert.Equal(new[]
            {
                new SparklePointAward(1, 4, SparklePointSource.LevelUp),
                new SparklePointAward(1, 5, SparklePointSource.LevelUp),
            }, awards);

            // An award that crosses no level pays nothing.
            ProgressionBank.Add(1, "Quest");
            Assert.Equal(5, s.SkillPoints);
            Assert.Equal(2, awards.Count);
        }
        finally
        {
            SparklePointRewards.Awarded -= cue;
            ProgressionBank.SkillPointsAwarded -= stat;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    }

    [Fact]
    public void AWebClaimThatCrossesALevelPaysItsSparkle()
    {
        var previous = CoreSettings.ServiceProvider;
        var service = Fresh(7, 0);
        var s = CoreSettings.Current;
        var awards = new List<SparklePointAward>();
        EventHandler<SparklePointAward> cue = (_, a) => awards.Add(a);
        SparklePointRewards.Awarded += cue;
        try
        {
            ProgressionBank.AddClaimed(XpCurve.GetXPForLevel(7, XpCurve.EpochOf(s)));
            Assert.Equal(8, s.PlayerLevel);
            Assert.Equal(1, s.SkillPoints);
            Assert.Equal(new SparklePointAward(1, 1, SparklePointSource.LevelUp), Assert.Single(awards));
        }
        finally
        {
            SparklePointRewards.Awarded -= cue;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    }
}
