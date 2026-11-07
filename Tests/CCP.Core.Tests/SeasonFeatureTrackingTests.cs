using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>WPF SeasonRecapService.TrackFeature (:183) and CurrentSeasonKey (:30), shared in Core for the other heads.</summary>
public class SeasonFeatureTrackingTests
{
    private static readonly DateTime Now = new(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-06", "2026-06")]
    [InlineData("season-6", "2026-07")]
    [InlineData(null, "2026-07")]
    public void Track_FreshBucketUsesSharedSeasonKey(string? serverSeason, string expected)
    {
        var s = new AppSettings { CurrentSeason = serverSeason };
        SeasonFeatureTracking.Track(s, SeasonFeatureKeys.Flash, null, Now);
        Assert.Equal(expected, s.SeasonStatsSeason);
    }

    [Fact]
    public void Track_CatalogFeature_NotesTheDayLog_OpensTheBucketOnce_AndCounts()
    {
        var s = new AppSettings { CurrentSeason = "2026-06" };
        var notes = new List<string>();
        SeasonFeatureTracking.Track(s, SeasonFeatureKeys.Flash, notes.Add, Now);
        Assert.Equal(new[] { "e_fl" }, notes);
        Assert.Equal("2026-06", s.SeasonStatsSeason);
        Assert.Equal(1, s.SeasonFeatureUse[SeasonFeatureKeys.Flash]);

        s.CurrentSeason = "2026-08";   // an open bucket is never re-keyed here (rollover is the recap's job)
        SeasonFeatureTracking.Track(s, SeasonFeatureKeys.Flash, notes.Add, Now);
        Assert.Equal("2026-06", s.SeasonStatsSeason);
        Assert.Equal(2, s.SeasonFeatureUse[SeasonFeatureKeys.Flash]);
    }

    [Fact]
    public void Track_PieceByPiece_NotesTheDayLogOnly_LikeWpf()
    {
        // Piece by Piece has a day-log event but no Catalog entry, so WPF stops after the note.
        var s = new AppSettings();
        var notes = new List<string>();
        SeasonFeatureTracking.Track(s, SeasonFeatureKeys.PieceByPiece, notes.Add, Now);
        SeasonFeatureTracking.Track(s, "not-a-feature", notes.Add, Now);
        Assert.Equal(new[] { "e_pb" }, notes);
        Assert.Empty(s.SeasonFeatureUse);
        Assert.True(string.IsNullOrEmpty(s.SeasonStatsSeason));
    }
}
