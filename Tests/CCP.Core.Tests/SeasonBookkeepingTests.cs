using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class SeasonBookkeepingTests
{
    [Fact]
    public void ConfirmedForwardSeasonSnapshotsBeforeClearingAndLaterWritesGoToNewBucket()
    {
        var s = new AppSettings { CurrentSeason = "1901-02", SeasonStatsSeason = "1901-01", LastSeasonResetSeen = "1901-01",
            SeasonConditioningMinutes = 20, TotalConditioningMinutes = 80, SeasonSessionsStarted = 2,
            SeasonActiveDays = new List<string> { "1901-01-02" }, SeasonPeakStreak = 3, SeasonPeakRank = 2,
            SeasonPeakRankTotal = 10, SeasonPeakLevel = 7, SeasonPointsSpent = 8,
            SeasonFeatureUse = new Dictionary<string, int> { ["test"] = 4 } };
        SeasonBookkeeping.Update(s, 42);
        var snap = SeasonRecapStore.Load("1901-01");
        Assert.NotNull(snap);
        Assert.Equal(20, snap.SeasonMinutes);
        Assert.Equal(80, snap.AllTimeMinutes);
        Assert.Equal(2, snap.SessionCount);
        Assert.Equal(4, snap.FeatureUse["test"]);
        Assert.Equal(42, snap.LifetimePointsSpent);
        Assert.Single(snap.ActiveDays);
        Assert.Equal("1901-02", s.SeasonStatsSeason);
        Assert.Equal("1901-02", s.LastSeasonResetSeen);
        Assert.Equal(0, s.SeasonConditioningMinutes);
        Assert.Equal(0, s.SeasonSessionsStarted);
        Assert.Empty(s.SeasonActiveDays);
        Assert.Empty(s.SeasonFeatureUse);
        Assert.Equal(0, s.SeasonPeakStreak + s.SeasonPeakRank + s.SeasonPeakRankTotal + s.SeasonPeakLevel + s.SeasonPointsSpent);
        ConditioningTimeTracker.AddConditioningTime(s, 3);
        SeasonBookkeeping.Update(s); // duplicate must not overwrite the snapshot or clear the new bucket
        Assert.Equal(3, s.SeasonConditioningMinutes);
        Assert.Equal(20, SeasonRecapStore.Load("1901-01")!.SeasonMinutes);
        s.CurrentSeason = "1900-12";
        SeasonBookkeeping.Update(s);
        s.CurrentSeason = "not-a-season";
        SeasonBookkeeping.Update(s);
        Assert.Equal("1901-02", s.SeasonStatsSeason);
        Assert.Equal(3, s.SeasonConditioningMinutes);
    }

    [Fact]
    public void FailedSnapshotDoesNotDiscardTheOldBucket()
    {
        var dir = Path.Combine(CorePaths.UserData, "season-recaps", "1902-01.json");
        Directory.CreateDirectory(dir);
        try
        {
            var s = new AppSettings { CurrentSeason = "1902-02", SeasonStatsSeason = "1902-01",
                LastSeasonResetSeen = "1902-01", SeasonConditioningMinutes = 5 };
            SeasonBookkeeping.Update(s);
            Assert.Equal("1902-01", s.SeasonStatsSeason);
            Assert.Equal("1902-01", s.LastSeasonResetSeen);
            Assert.Equal(5, s.SeasonConditioningMinutes);
        }
        finally { Directory.Delete(dir); }
    }
}
