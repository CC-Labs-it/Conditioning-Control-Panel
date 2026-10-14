using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;
public sealed class EnhancementAnalyticsTests
{
    [Fact]
    public void SeriesUsesSnapshotForDuplicateLiveKeyAndCapsInAscendingOrder()
    {
        var s = new AppSettings { SeasonStatsSeason = "2026-09", SeasonConditioningMinutes = 150, PlayerLevel = 17 };
        var history = new[]
        {
            new SeasonRecapSnapshot { SeasonKey = "2026-09", SeasonMinutes = 90, PeakLevel = 9 },
            new SeasonRecapSnapshot { SeasonKey = "2026-07", SeasonMinutes = 70 },
            new SeasonRecapSnapshot { SeasonKey = "2026-08", SeasonMinutes = 80 },
        };
        var series = EnhancementAnalytics.Series(s, history, "2026-10", 2);
        Assert.Equal(new[] { "2026-08", "2026-09" }, series.Select(x => x.Key));
        Assert.Equal(90, series[1].Minutes);
        s.SeasonStatsSeason = null;
        s.SeasonActiveDays.Add("2026-10-01");
        series = EnhancementAnalytics.Series(s, history, "2026-10", 2);
        Assert.Equal(17, series[1].Level);
        Assert.Equal(new[] { "2026-10-01" }, series[1].ActiveDays);
        series[1].ActiveDays.Clear();
        Assert.Single(s.SeasonActiveDays);
    }

    [Fact]
    public void RewindKeepsLiveFirstSkipsDuplicateAndPreservesSchemaAvailability()
    {
        var s = new AppSettings { SeasonStatsSeason = "2026-09", SeasonPeakRank = 1, SeasonPeakRankTotal = 250,
            SeasonPeakLevel = 30, PlayerLevel = 10, SeasonPointsSpent = 12 };
        var rows = EnhancementAnalytics.Rewind(s, new[]
        {
            new SeasonRecapSnapshot { SeasonKey = "2026-09" },
            new SeasonRecapSnapshot { SeasonKey = "2026-08", Schema = 1, PeakLevel = 9, PointsSpentSeason = 7 },
        }, "2026-10");
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsLive);
        Assert.Equal(30, rows[0].Level);
        Assert.Equal(1, rows[0].Pct);
        Assert.Equal(12, rows[0].Spent);
        Assert.False(rows[1].IsLive);
        Assert.False(rows[1].HasV2);
        Assert.Equal(0, EnhancementAnalytics.Percentile(0, 100));
        Assert.Equal(99, EnhancementAnalytics.Percentile(100, 100));
    }

    [Fact]
    public void RecordsAndFeatureLifetimeIncludeLiveAndSnapshotsWithoutMutatingCounters()
    {
        var s = new AppSettings { SeasonStatsSeason = "2026-10", SeasonConditioningMinutes = 300,
            PlayerLevel = 50, SeasonPeakRank = 10 };
        s.SeasonFeatureUse[SeasonFeatureKeys.Flash] = 3;
        var old = new SeasonRecapSnapshot { SeasonKey = "2026-09", SeasonMinutes = 600, PeakRank = 2, PeakLevel = 40 };
        old.FeatureUse[SeasonFeatureKeys.Flash] = 7;
        old.FeatureUse[SeasonFeatureKeys.Video] = 2;
        var records = EnhancementAnalytics.BestRecords(s, new[] { old }, "2026-11");
        Assert.Equal("2026-09", records.Time.Key);
        Assert.Equal("2026-09", records.Rank.Key);
        Assert.Equal(50, records.Level.Level);
        var life = EnhancementAnalytics.LifetimeFeatureUse(s, new[] { old });
        Assert.Equal(10, life[SeasonFeatureKeys.Flash]);
        Assert.Equal(2, life[SeasonFeatureKeys.Video]);
        Assert.Equal(3, s.SeasonFeatureUse[SeasonFeatureKeys.Flash]);
        Assert.Equal(7, old.FeatureUse[SeasonFeatureKeys.Flash]);
        Assert.Equal($"1.0 {Loc.Get("label_hrs")}", EnhancementAnalytics.FormatMinutes(60));
        Assert.Equal($"59 {Loc.Get("label_min_abbrev")}", EnhancementAnalytics.FormatMinutes(59));
        Assert.Equal("sep", EnhancementAnalytics.MonthLabel("2026-09"));
        Assert.Equal("unknown", EnhancementAnalytics.MonthLabel("unknown"));
        s.CurrentSeason = "2026-04";
        Assert.Equal("2026-04", EnhancementAnalytics.CurrentSeasonKey(s));
    }
}
