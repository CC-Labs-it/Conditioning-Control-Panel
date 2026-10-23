using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF SeasonRecapService.TrackFeature + CurrentSeasonKey (Services/Progression/SeasonRecapService.cs:30,:192).
public sealed class SeasonFeatureCreditTests
{
    private static readonly DateTime Now = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SeasonKey_PrefersAWellFormedServerKey_ElseTheWallClockMonth()
    {
        Assert.Equal("2026-06", SeasonFeatureCredit.CurrentSeasonKey(new AppSettings { CurrentSeason = "2026-06" }, Now));
        Assert.Equal("2026-07", SeasonFeatureCredit.CurrentSeasonKey(new AppSettings { CurrentSeason = "summer" }, Now));
        Assert.Equal("2026-07", SeasonFeatureCredit.CurrentSeasonKey(null, Now));
    }

    [Fact]
    public void Track_NotesTheDayEvent_AndCountsOnlyCatalogFeaturesInTheSeasonBucket()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-season-credit-").FullName;
        try
        {
            using var log = new FeatureDayLogService(() => null, Path.Combine(dir, "log.json"));
            var s = new AppSettings { CurrentSeason = "2026-06" };

            SeasonFeatureCredit.Track(s, SeasonFeatureKeys.BlinkTrainer, log, Now);
            var today = log.Log.GetOrAddDay(DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(1, today.Get("e_bl"));
            Assert.False(s.SeasonFeatureUse.ContainsKey(SeasonFeatureKeys.BlinkTrainer));   // not in the recap Catalog

            SeasonFeatureCredit.Track(s, SeasonFeatureKeys.Flash, log, Now);
            Assert.Equal(1, today.Get("e_fl"));
            Assert.Equal(1, s.SeasonFeatureUse[SeasonFeatureKeys.Flash]);
            Assert.Equal("2026-06", s.SeasonStatsSeason);
        }
        finally { Directory.Delete(dir, true); }
    }
}
