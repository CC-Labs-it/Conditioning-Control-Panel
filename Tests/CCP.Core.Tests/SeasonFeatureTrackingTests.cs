using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class SeasonFeatureTrackingTests
{
    [Fact]
    public void SessionFlagsPersistAndFeedBrainDrainAndRollover()
    {
        var s = new AppSettings { CurrentSeason = "1912-01", LastSeasonResetSeen = "1912-01" };
        var flags = new SessionSettings { FlashEnabled = true, MandatoryVideosEnabled = true,
            SubliminalEnabled = true, SpiralEnabled = true, PinkFilterEnabled = true, BubblesEnabled = true,
            BubbleCountEnabled = true, BouncingTextEnabled = true, LockCardEnabled = true, MindWipeEnabled = true };
        var events = new List<string>();
        SeasonFeatureTracking.RecordSession(s, flags, events.Add);
        Assert.Equal("1912-01", s.SeasonStatsSeason);
        Assert.Equal(1, s.SeasonSessionsStarted);
        Assert.Equal(SeasonFeatureKeys.TotalCount, s.SeasonFeatureUse.Count);
        Assert.All(s.SeasonFeatureUse.Values, n => Assert.Equal(1, n));
        Assert.Equal(SeasonFeatureKeys.TotalCount, events.Count);
        Assert.Equal(1, events.Count(e => e == SeasonFeatureKeys.ToDayLogEvent(SeasonFeatureKeys.Overlay)));
        s = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.Equal(1, EnhancementAnalytics.LifetimeFeatures(s, Array.Empty<SeasonRecapSnapshot>())[SeasonFeatureKeys.Flash]);
        s.CurrentSeason = "1912-02";
        SeasonBookkeeping.Update(s);
        var snap = SeasonRecapStore.Load("1912-01")!;
        Assert.Equal(1, snap.SessionCount);
        Assert.Equal(1, snap.FeatureUse[SeasonFeatureKeys.Overlay]);
        Assert.Empty(s.SeasonFeatureUse);
        Assert.Equal(1, EnhancementAnalytics.LifetimeFeatures(s, new[] { snap })[SeasonFeatureKeys.Flash]);
    }

    [Fact]
    public void PendingSnapshotKeepsLaterCreditsAcrossSerialization()
    {
        var s = new AppSettings { SeasonStatsSeason = "1914-01", PendingSeasonSnapshot = new SeasonRecapSnapshot
            { SeasonKey = "1914-01", SessionCount = 2, FeatureUse = new() { [SeasonFeatureKeys.Flash] = 2 } } };
        SeasonFeatureTracking.RecordSession(s, new SessionSettings { FlashEnabled = true });
        Assert.Equal(3, s.PendingSeasonSnapshot!.SessionCount);
        s = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
        Assert.Equal(3, s.PendingSeasonSnapshot!.SessionCount);
        Assert.Equal(3, s.PendingSeasonSnapshot.FeatureUse[SeasonFeatureKeys.Flash]);
    }

    [Fact]
    public void RunnerCountsStartsButNotPauseOrResume()
    {
        var s = CoreSettings.Current;
        var oldSeason = s.SeasonStatsSeason;
        var oldCount = s.SeasonSessionsStarted;
        var oldFeatures = s.SeasonFeatureUse;
        var events = new List<string>();
        var runner = new SessionRunner(new SessionLogService()) { NoteFeatureDayEvent = events.Add };
        try
        {
            s.SeasonStatsSeason = "1913-01";
            s.SeasonSessionsStarted = 0;
            s.SeasonFeatureUse = new();
            var session = new Session { Id = "season-test", Name = "Season test", DurationMinutes = 1,
                Settings = new SessionSettings { FlashEnabled = true, PinkFilterEnabled = true, SpiralEnabled = true } };
            runner.Start(session);
            runner.Pause();
            runner.Resume();
            Assert.Equal(1, s.SeasonSessionsStarted);
            Assert.Equal(2, s.SeasonFeatureUse.Count);
            Assert.Equal(1, s.SeasonFeatureUse[SeasonFeatureKeys.Overlay]);
            Assert.Equal(2, events.Count);
            runner.Stop();
            runner.Start(session);
            Assert.Equal(2, s.SeasonSessionsStarted);
            Assert.Equal(2, s.SeasonFeatureUse[SeasonFeatureKeys.Flash]);
            Assert.Equal(4, events.Count);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            s.SeasonStatsSeason = oldSeason;
            s.SeasonSessionsStarted = oldCount;
            s.SeasonFeatureUse = oldFeatures;
        }
    }
}
