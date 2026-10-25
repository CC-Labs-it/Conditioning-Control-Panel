using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EnhancementAnalyticsTests
{
    [Fact]
    public void ChartsRewindAndRecordsRespectLiveBucketHistoricalSchemaAndCaps()
    {
        var s = new AppSettings { SeasonStatsSeason = "2026-10", SeasonConditioningMinutes = 180,
            SeasonPeakLevel = 12, PlayerLevel = 20, SeasonPeakRank = 1, SeasonPeakRankTotal = 100,
            SeasonPointsSpent = 55, SeasonActiveDays = new List<string> { "2026-10-01" } };
        Assert.Equal("2026-11", EnhancementAnalytics.CurrentSeasonKey(s, new System.DateTime(2026, 11, 1)));
        s.CurrentSeason = "2026-07";
        Assert.Equal("2026-07", EnhancementAnalytics.CurrentSeasonKey(s, new System.DateTime(2026, 11, 1)));
        var snapshots = new[] {
            new SeasonRecapSnapshot { SeasonKey = "2026-09", SeasonMinutes = 240, PeakRank = 3, PeakLevel = 40, Schema = 2 },
            new SeasonRecapSnapshot { SeasonKey = "2026-08", SeasonMinutes = 60, PeakRank = 0, Schema = 1 } };
        var series = EnhancementAnalytics.Series(s, "2026-11", snapshots, 2);
        Assert.Equal(new[] { "2026-09", "2026-10" }, series.Select(x => x.Key));
        Assert.Equal(20, series[1].Level);
        series[1].ActiveDays.Clear();
        Assert.Single(s.SeasonActiveDays); // chart data cannot clear live counters
        var rows = EnhancementAnalytics.Rewind(s, "2026-11", snapshots);
        Assert.Equal(new[] { "2026-10", "2026-09", "2026-08" }, rows.Select(x => x.Key));
        Assert.True(rows[0].IsLive);
        Assert.Equal(1, rows[0].Pct);
        Assert.Equal(55, rows[0].Spent);
        Assert.False(rows[2].HasV2);
        var records = EnhancementAnalytics.Records(s, "2026-11", snapshots);
        Assert.Equal("2026-09", EnhancementAnalytics.BestTime(records).Key);
        Assert.Equal("2026-10", EnhancementAnalytics.BestRank(records)!.Value.Key);
        Assert.Equal(40, EnhancementAnalytics.BestLevel(records)!.Value.Level);
        Assert.Equal("sep", EnhancementAnalytics.MonthLabel("2026-09"));
        Assert.Equal("unknown", EnhancementAnalytics.MonthLabel("unknown"));
        Assert.Equal(99, EnhancementAnalytics.PercentileFor(101, 100));
        Assert.Equal(0, EnhancementAnalytics.PercentileFor(0, 100));
    }

    [Fact]
    public void DesyncedChartBucketIsNotDuplicatedAndFeatureTotalsDoNotMutateLiveCounts()
    {
        var s = new AppSettings { SeasonStatsSeason = "2026-10",
            SeasonFeatureUse = new Dictionary<string, int> { [SeasonFeatureKeys.Flash] = 2 } };
        var snapshots = new[] { new SeasonRecapSnapshot { SeasonKey = "2026-10", SeasonMinutes = 99,
            FeatureUse = new Dictionary<string, int> { [SeasonFeatureKeys.Flash] = 3, [SeasonFeatureKeys.Video] = 7 } } };
        Assert.Equal(99, Assert.Single(EnhancementAnalytics.Series(s, "2026-11", snapshots)).Minutes);
        Assert.Single(EnhancementAnalytics.Rewind(s, "2026-11", snapshots));
        var total = EnhancementAnalytics.LifetimeFeatures(s, snapshots);
        Assert.Equal(5, total[SeasonFeatureKeys.Flash]);
        Assert.Equal(7, total[SeasonFeatureKeys.Video]);
        Assert.Equal(2, s.SeasonFeatureUse[SeasonFeatureKeys.Flash]);
        Assert.Equal(SeasonFeatureKeys.Video, EnhancementAnalytics.OrderedFeatures(total).First().Key);
    }
}
