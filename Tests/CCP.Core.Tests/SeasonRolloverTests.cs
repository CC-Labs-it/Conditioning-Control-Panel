using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

public class SeasonRolloverTests
{
    [Fact]
    public void CoreDoesNotExportWpfSeasonRecapServiceName()
    {
        Assert.Null(typeof(AppSettings).Assembly.GetType("ConditioningControlPanel.Services.SeasonRecapService"));
    }

    [Fact]
    public void BookkeepingRequiresConfirmedSeasonAndClearsPendingOnlyWhenConfirmed()
    {
        var s = new AppSettings { CurrentSeason = "bad", SeasonResetPending = true };
        Assert.False(SeasonRollover.UpdateBookkeeping(s));
        Assert.True(s.SeasonResetPending);
        s.CurrentSeason = "2026-08";
        s.LastSeasonResetSeen = "2026-08";
        Assert.True(SeasonRollover.UpdateBookkeeping(s));
        Assert.False(s.SeasonResetPending);
        Assert.False(SeasonRollover.UpdateBookkeeping(s));
    }

    [Fact]
    public void BookkeepingInitializesAndRollsOnlyAfterSnapshotSave()
    {
        var s = new AppSettings { CurrentSeason = "2026-07" };
        Assert.True(SeasonRollover.UpdateBookkeeping(s));
        Assert.Equal("2026-07", s.SeasonStatsSeason);
        s.CurrentSeason = "2026-08";
        s.SeasonSessionsStarted = 3;
        Assert.False(SeasonRollover.UpdateBookkeeping(s, _ => false));
        Assert.Equal("2026-07", s.LastSeasonResetSeen);
        Assert.True(SeasonRollover.UpdateBookkeeping(s, snap => snap.SessionCount == 3));
        Assert.Equal("2026-08", s.SeasonStatsSeason);
        Assert.Equal("2026-08", s.LastSeasonResetSeen);
        Assert.Equal(0, s.SeasonSessionsStarted);
    }

    [Theory]
    [InlineData(0, 0, 17)]
    [InlineData(4, 100, 4)]
    public void SnapshotKeepsHeadValuesAndPrefersSeasonRank(int rank, int total, int expectedPercentile)
    {
        var s = new AppSettings { CurrentSeason = "2098-08", SeasonStatsSeason = "2098-07",
            SeasonSessionsStarted = 3, SeasonPeakRank = rank, SeasonPeakRankTotal = total };
        SeasonRecapSnapshot? saved = null;
        Assert.True(SeasonRollover.UpdateBookkeeping(s, snap => { saved = snap; return true; },
            handle: "Tester", isSupporter: true, lifetimePointsSpent: 12345, fallbackPercentile: 17));
        Assert.NotNull(saved);
        Assert.Multiple(
            () => Assert.Equal("Tester", saved.Handle),
            () => Assert.True(saved.IsSupporter),
            () => Assert.Equal(12345, saved.LifetimePointsSpent),
            () => Assert.Equal(expectedPercentile, saved.Percentile),
            () => Assert.Equal(3, saved.SessionCount),
            () => Assert.Equal(0, s.SeasonSessionsStarted));
    }

    [Theory]
    [InlineData(null)] [InlineData("bad")] [InlineData("2026-06")] [InlineData("2026-07")]
    public void RequiresConfirmedForwardKey(string? key)
    {
        var s = new AppSettings { CurrentSeason = key, SeasonStatsSeason = "2026-07", SeasonSessionsStarted = 3 };
        Assert.Null(SeasonRollover.CaptureAndRollover(s, key, _ => throw new System.Exception()));
        Assert.Equal("2026-07", s.SeasonStatsSeason);
        Assert.Equal(3, s.SeasonSessionsStarted);
    }

    [Fact]
    public void UnconfirmedKeyCannotRoll()
    {
        var s = new AppSettings { SeasonStatsSeason = "2026-07", SeasonSessionsStarted = 3 };
        Assert.Null(SeasonRollover.CaptureAndRollover(s, "2026-08"));
        Assert.Equal(3, s.SeasonSessionsStarted);
    }

    [Fact]
    public void SavesMeaningfulSnapshotBeforeClearing()
    {
        var path = System.IO.Path.Combine(CorePaths.UserData, "season-recaps", "2098-07.json");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.Delete(path);
        try
        {
            var s = new AppSettings { CurrentSeason = "2098-08", SeasonStatsSeason = "2098-07", SeasonSessionsStarted = 3 };
            var snap = SeasonRollover.CaptureAndRollover(s, "2098-08", x => {
                Assert.Equal(3, s.SeasonSessionsStarted);
                Assert.Equal("2098-07", x.SeasonKey);
                Assert.True(SeasonRecapStore.TrySave(x));
                return true;
            });
            Assert.NotNull(snap);
            Assert.Equal(3, SeasonRecapStore.Load("2098-07")!.SessionCount);
            Assert.Equal(0, s.SeasonSessionsStarted);
            Assert.Equal("2098-08", s.SeasonStatsSeason);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void FailedSaveKeepsBucket()
    {
        var s = new AppSettings { CurrentSeason = "2026-08", SeasonStatsSeason = "2026-07", SeasonSessionsStarted = 3 };
        Assert.Null(SeasonRollover.CaptureAndRollover(s, "2026-08", _ => false));
        Assert.Equal(3, s.SeasonSessionsStarted);
        Assert.Equal("2026-07", s.SeasonStatsSeason);
    }

    [Fact]
    public void EmptyBucketAdvancesWithoutSaving()
    {
        var s = new AppSettings { CurrentSeason = "2026-08", SeasonStatsSeason = "2026-07" };
        Assert.Null(SeasonRollover.CaptureAndRollover(s, "2026-08", _ => throw new System.Exception()));
        Assert.Equal("2026-08", s.SeasonStatsSeason);
    }
}
