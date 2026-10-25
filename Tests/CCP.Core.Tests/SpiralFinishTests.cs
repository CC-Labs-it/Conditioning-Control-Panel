using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SpiralFinishTests
{
    [Theory]
    [InlineData("https://app.cclabs.app/embed/spiral?mode=map", true)]
    [InlineData("http://app.cclabs.app/embed/spiral", false)]
    [InlineData("https://app.cclabs.app.evil.test/", false)]
    [InlineData("https://app.cclabs.app:444/", false)]
    [InlineData("https://fake@app.cclabs.app/", false)]
    public void MapFencesEveryNavigation(string url, bool allowed) => Assert.Equal(allowed, SpiralEmbedState.Allows(new Uri(url)));

    [Fact]
    public void MapForwardsUnknownFieldsAndDateStrings_AndOmitsAbsentRaw()
    {
        var raw = """{"future":{"at":"2026-08-16T00:00:00Z"},"stations":[{"id":"server-id"}]}""";
        var state = SpiralEmbedState.Build(new DescentBlock { DevotionDays = 12, RawJson = raw }, true, PerformanceTier.Balanced);
        Assert.Equal("spiral:state", state.Value<string>("type"));
        Assert.Equal(12, state.Value<int>("devotion_days"));
        Assert.Equal(JTokenType.String, state["descent"]!["future"]!["at"]!.Type);
        Assert.Equal("2026-08-16T00:00:00Z", (string?)state["descent"]!["future"]!["at"]);
        Assert.Equal("server-id", (string?)state["descent"]!["stations"]![0]!["id"]);
        Assert.Null(state["lit_station_ids"]);
        Assert.True(state.Value<bool>("reduced_motion"));
        Assert.Equal("mid", state.Value<string>("perf_tier"));
        var empty = SpiralEmbedState.Build(null, false, PerformanceTier.Quality);
        Assert.Null(empty["descent"]);
        Assert.Equal(0, empty.Value<int>("stage"));
        Assert.Equal(1, empty.Value<double>("relapse_multiplier"));
        Assert.Equal("high", empty.Value<string>("perf_tier"));
        Assert.Equal("low", (string?)SpiralEmbedState.Build(null, false, PerformanceTier.Performance)["perf_tier"]);
    }

    [Fact]
    public void SilentRestoreRepricesServerBasis_PreservesUnlocks_ArmsChoiceLast_AndIsIdempotent()
    {
        var s = new AppSettings { PlayerLevel = 9, PlayerXP = 123, HighestLevelEver = 99, DescentMigrationOffered = true,
            LastConfirmedServerXp = 10000 };
        var offer = JObject.Parse("""{"descent_migration":{"required":true,"total_xp_earned":5000,"restore_basis_xp":8000,"devotion_days":12}}""");
        var result = DescentMigration.Resolve("restore", new DescentMigrationOffer { TotalXpEarned = 5000, RestoreBasisXp = 8000 });
        var now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(DescentAutoRestore.Apply(s, new JObject(), null, now));
        Assert.True(DescentAutoRestore.Apply(s, offer, new DescentBlock(), now));
        Assert.Equal(result.Level, s.PlayerLevel);
        Assert.Equal(result.XpIntoLevel, s.PlayerXP);
        Assert.Equal(9, s.DescentPreMigrationLevel);
        Assert.Equal(8000, s.DescentPreMigrationLifetimeXp);
        Assert.Equal(99, s.HighestLevelEver);
        Assert.Equal(0, s.LastConfirmedServerXp);
        Assert.Equal(now, s.DescentAnchorUtc);
        Assert.Equal("restore", s.PendingDescentMigrationChoice);
        Assert.False(s.DescentMigrationCompleted); // ack alone settles this
        Assert.False(ProfileSpiral.IsWithheld(s));
        Assert.Equal(DescentCycleXp.CycleXpBonus, s.DescentCycleXpBonus);
        Assert.False(DescentAutoRestore.Apply(s, offer, null, now.AddDays(1)));
        s.PendingDescentMigrationChoice = null; s.DescentMigrationCompleted = true;
        Assert.False(DescentAutoRestore.Apply(s, offer, null, now));
    }

    [Fact]
    public void DripRequiresAck_AndReleasesOneStagePerLocalDay()
    {
        var s = new AppSettings { DescentPendingStageCeremonies = new() { 1, 2, 3 } };
        var day = new DateTime(2026, 10, 25);
        Assert.Null(DescentAutoRestore.Drip(s, day));
        s.DescentMigrationCompleted = true;
        Assert.Equal(1, DescentAutoRestore.Drip(s, day));
        Assert.Null(DescentAutoRestore.Drip(s, day.AddHours(12)));
        Assert.Equal(2, DescentAutoRestore.Drip(s, day.AddDays(1)));
        Assert.Equal(new[] { 3 }, s.DescentPendingStageCeremonies);
    }
}
