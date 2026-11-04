using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    public void SessionStartsSurviveFailedSnapshotAndRollIntoTheirOwnSeason()
    {
        var path = Path.Combine(CorePaths.UserData, "season-recaps", "1911-01.json");
        Directory.CreateDirectory(path);
        var s = new AppSettings { CurrentSeason = "1911-02", SeasonStatsSeason = "1911-01",
            LastSeasonResetSeen = "1911-01", SeasonSessionsStarted = 2 };
        try
        {
            SeasonBookkeeping.RecordSessionStart(s, DateTime.UtcNow);
            Assert.Equal(2, s.SeasonSessionsStarted);
            Assert.Equal(1, s.PendingSeasonSessions["1911-02"]);
            s = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
            Directory.Delete(path);
            SeasonBookkeeping.Update(s);
            Assert.Equal(2, SeasonRecapStore.Load("1911-01")!.SessionCount);
            Assert.Equal(1, s.SeasonSessionsStarted);
            s.CurrentSeason = "1911-03";
            SeasonBookkeeping.Update(s);
            Assert.Equal(1, SeasonRecapStore.Load("1911-02")!.SessionCount);
            Assert.Equal(0, s.SeasonSessionsStarted);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path); }
    }

    [Fact]
    public void ConcurrentSeasonAdoptionWaitsForTrackerGate()
    {
        var s = new AppSettings { CurrentSeason = "1908-01" };
        using var started = new ManualResetEventSlim();
        Task adoption;
        bool completedWhileLocked;
        lock (SeasonBookkeeping.Gate)
        {
            adoption = Task.Run(() => { started.Set(); s.CurrentSeason = "1908-02"; });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            completedWhileLocked = adoption.Wait(TimeSpan.FromMilliseconds(500));
        }
        Assert.True(adoption.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(completedWhileLocked);
        Assert.Equal("1908-02", s.CurrentSeason);
    }

    [Fact]
    public void TrackerReconcilesAdoptedSeasonBeforeCreditingMinutes()
    {
        var s = new AppSettings { CurrentSeason = "1909-01", SeasonStatsSeason = "1909-01",
            LastSeasonResetSeen = "1909-01", SeasonConditioningMinutes = 5, TotalConditioningMinutes = 10 };
        Task.Run(() => s.CurrentSeason = "1909-02").GetAwaiter().GetResult();
        ConditioningTimeTracker.AddConditioningTime(s, 3);
        var snapshot = SeasonRecapStore.Load("1909-01");
        Assert.NotNull(snapshot);
        Assert.Equal(5, snapshot.SeasonMinutes);
        Assert.Equal(10, snapshot.AllTimeMinutes);
        Assert.Equal("1909-02", s.SeasonStatsSeason);
        Assert.Equal(3, s.SeasonConditioningMinutes);
        Assert.Equal(13, s.TotalConditioningMinutes);
    }

    [Fact]
    public void FailedSnapshotKeepsLaterMinutesForTheNewSeasonAndRetries()
    {
        var path = Path.Combine(CorePaths.UserData, "season-recaps", "1904-01.json");
        Directory.CreateDirectory(path);
        var s = new AppSettings { CurrentSeason = "1904-02", SeasonStatsSeason = "1904-01",
            LastSeasonResetSeen = "1904-01", SeasonConditioningMinutes = 5, TotalConditioningMinutes = 10 };
        try
        {
            SeasonBookkeeping.Update(s);
            ConditioningTimeTracker.AddConditioningTime(s, 3);
            Assert.Equal(5, s.SeasonConditioningMinutes);
            s.CurrentSeason = null; // a lost key must not redirect later minutes to the ended bucket
            ConditioningTimeTracker.AddConditioningTime(s, 1);
            Assert.Equal(5, s.SeasonConditioningMinutes);
            s = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
            Directory.Delete(path);
            ConditioningTimeTracker.AddConditioningTime(s, 2); // the persisted confirmed target can retry without a live key
            Assert.Equal("1904-02", s.SeasonStatsSeason);
            Assert.Equal(6, s.SeasonConditioningMinutes);
            Assert.Equal(5, SeasonRecapStore.Load("1904-01")!.SeasonMinutes);
            Assert.Equal(16, s.TotalConditioningMinutes);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path); }
    }

    [Fact]
    public void PendingMinutesAcrossSeveralSeasonsStaySeparate()
    {
        var path = Path.Combine(CorePaths.UserData, "season-recaps", "1907-01.json");
        Directory.CreateDirectory(path);
        var s = new AppSettings { CurrentSeason = "1907-02", SeasonStatsSeason = "1907-01",
            LastSeasonResetSeen = "1907-01", SeasonConditioningMinutes = 5, TotalConditioningMinutes = 5 };
        try
        {
            SeasonBookkeeping.Update(s, 42, 15);
            ConditioningTimeTracker.AddConditioningTime(s, 3);
            s.CurrentSeason = "1907-03";
            SeasonBookkeeping.Update(s);
            ConditioningTimeTracker.AddConditioningTime(s, 4);
            s = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
            Directory.Delete(path);
            SeasonBookkeeping.Update(s);
            Assert.Equal(5, SeasonRecapStore.Load("1907-01")!.SeasonMinutes);
            var february = SeasonRecapStore.Load("1907-02")!;
            Assert.Equal(3, february.SeasonMinutes);
            Assert.Equal(8, february.AllTimeMinutes);
            Assert.Equal(15, february.Percentile);
            Assert.Equal("1907-03", s.SeasonStatsSeason);
            Assert.Equal(4, s.SeasonConditioningMinutes);
            Assert.Empty(s.PendingSeasonMinutes);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path); }
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
