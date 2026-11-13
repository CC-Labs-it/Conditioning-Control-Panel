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
