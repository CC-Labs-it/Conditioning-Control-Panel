using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Instant unlock for a tier bought on the site: the server's folded <c>effective_tier</c> is
/// read off the heartbeat and the profile, a rise is applied at once, a drop ends the grace it no
/// longer backs, and each tier gets its own celebration card.
/// </summary>
public class EntitlementTierRuleTests
{
    [Theory]
    [InlineData("{\"ok\":true,\"effective_tier\":2}", 2)]
    [InlineData("{\"effective_tier\":1,\"progression\":{\"level\":3}}", 1)]
    [InlineData("{\"effective_tier\":0}", 0)]
    public void Heartbeat_ReadsTopLevelTier(string body, int expected)
        => Assert.Equal(expected, EntitlementTierRule.ParseHeartbeatTier(body));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("{\"effective_tier\":null}")]
    [InlineData("{\"effective_tier\":\"2\"}")]
    [InlineData("{\"effective_tier\":1.5}")]
    [InlineData("{\"effective_tier\":3}")]
    [InlineData("{\"effective_tier\":-1}")]
    [InlineData("{\"effective_tier\":99999999999999}")]
    [InlineData("{\"progression\":{\"effective_tier\":2}}")]
    public void Heartbeat_GarbageIsNoSignal(string? body)
        => Assert.Null(EntitlementTierRule.ParseHeartbeatTier(body));

    [Fact]
    public void ProfileNode_TierParsesThroughTheSameRule()
    {
        var user = JObject.Parse("{\"patreon_tier\":0,\"effective_tier\":2}");
        Assert.Equal(2, EntitlementTierRule.ParseTier(user["effective_tier"]));
        Assert.Null(EntitlementTierRule.ParseTier(JObject.Parse("{}")["effective_tier"]));
    }

    [Theory]
    [InlineData(0, 1, EntitlementVerdict.Rise)]
    [InlineData(0, 2, EntitlementVerdict.Rise)]
    [InlineData(1, 2, EntitlementVerdict.Rise)]
    [InlineData(1, 1, EntitlementVerdict.Same)]
    [InlineData(0, 0, EntitlementVerdict.Same)]
    [InlineData(2, 1, EntitlementVerdict.Lower)]
    [InlineData(1, 0, EntitlementVerdict.Lower)]
    [InlineData(5, 2, EntitlementVerdict.Same)]   // a corrupt stored tier clamps, never reads as a drop
    [InlineData(-3, 1, EntitlementVerdict.Rise)]
    public void Decide(int stored, int server, EntitlementVerdict expected)
        => Assert.Equal(expected, EntitlementTierRule.Decide(stored, server));

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Decide_NoReadingIsNoSignal(int? server)
        => Assert.Equal(EntitlementVerdict.NoSignal, EntitlementTierRule.Decide(1, server));

    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Grace_Tier1StampsPremiumOnly()
    {
        var s = new AppSettings();
        EntitlementTierRule.ExtendGrace(s, 1, Now);
        Assert.Equal(Now.AddDays(14), s.PatreonPremiumValidUntil);
        Assert.Null(s.PatreonLabValidUntil);
    }

    [Fact]
    public void Grace_Tier2StampsBoth()
    {
        var s = new AppSettings();
        EntitlementTierRule.ExtendGrace(s, 2, Now);
        Assert.Equal(Now.AddDays(14), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddDays(14), s.PatreonLabValidUntil);
    }

    [Fact]
    public void Grace_NeverShortensAndTier0TouchesNothing()
    {
        var later = Now.AddDays(40);
        var s = new AppSettings { PatreonPremiumValidUntil = later, PatreonLabValidUntil = later };
        EntitlementTierRule.ExtendGrace(s, 2, Now);
        Assert.Equal(later, s.PatreonPremiumValidUntil);
        Assert.Equal(later, s.PatreonLabValidUntil);

        var free = new AppSettings();
        EntitlementTierRule.ExtendGrace(free, 0, Now);
        Assert.Null(free.PatreonPremiumValidUntil);
        Assert.Null(free.PatreonLabValidUntil);
    }

    [Fact]
    public void Celebration_KeysPerTier()
    {
        Assert.Null(TierCelebration.KeyFor(0));
        Assert.Equal("premium-celebration-t1", TierCelebration.KeyFor(1));
        Assert.Equal("premium-celebration-t2", TierCelebration.KeyFor(2));
        // The legacy key is the one FeatureIntroPopup has always spent.
        Assert.Equal(FeatureIntroPopup.CelebrationKey, TierCelebration.LegacyKey);
    }

    [Theory]
    // fresh install: owed on launch and on a rise
    [InlineData(new string[0], 1, false, true)]
    [InlineData(new string[0], 2, true, true)]
    [InlineData(new string[0], 0, true, false)]
    // an install celebrated before per-tier keys: the launch re-check does not repeat it...
    [InlineData(new[] { "premium-celebration" }, 1, false, false)]
    [InlineData(new[] { "premium-celebration" }, 2, false, false)]
    // ...but a live purchase does celebrate (a past patron coming back, or Basic -> Prime)
    [InlineData(new[] { "premium-celebration" }, 1, true, true)]
    [InlineData(new[] { "premium-celebration" }, 2, true, true)]
    // Basic seen, Prime owed; each tier only once
    [InlineData(new[] { "premium-celebration-t1" }, 2, true, true)]
    [InlineData(new[] { "premium-celebration-t1" }, 2, false, true)]
    [InlineData(new[] { "premium-celebration-t1" }, 1, true, false)]
    [InlineData(new[] { "premium-celebration-t2" }, 2, true, false)]
    public void Celebration_IsOwed(string[] seen, int tier, bool onRise, bool expected)
        => Assert.Equal(expected, TierCelebration.IsOwed(new List<string>(seen), tier, onRise));

    [Fact]
    public void CloseGrace_TierZeroEndsBothStamps()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        Assert.True(EntitlementTierRule.CloseGrace(s, 0, Now));
        Assert.Equal(Now, s.PatreonPremiumValidUntil);
        Assert.Equal(Now, s.PatreonLabValidUntil);
    }

    [Fact]
    public void CloseGrace_BasicKeepsPremiumEndsLab()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        Assert.True(EntitlementTierRule.CloseGrace(s, 1, Now));
        Assert.Equal(Now.AddDays(9), s.PatreonPremiumValidUntil);
        Assert.Equal(Now, s.PatreonLabValidUntil);
    }

    [Fact]
    public void CloseGrace_PrimeIncludingWhitelistEndsNothing()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddHours(25), PatreonLabValidUntil = Now.AddDays(9) };
        Assert.False(EntitlementTierRule.CloseGrace(s, 2, Now));
        Assert.Equal(Now.AddHours(25), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddDays(9), s.PatreonLabValidUntil);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(-1)]
    public void CloseGrace_NoReadingKeepsTheOfflineWindow(int? server)
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        Assert.False(EntitlementTierRule.CloseGrace(s, server, Now));
        Assert.Equal(Now.AddDays(9), s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void CloseGrace_AnEndedStampKeepsItsDay()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(-3) };
        Assert.False(EntitlementTierRule.CloseGrace(s, 0, Now));
        Assert.Equal(Now.AddDays(-3), s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void CloseGrace_ThenTheClosedLapseRowOwes()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        EntitlementTierRule.CloseGrace(s, 0, Now);
        var reading = ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.DecideLapse(
            2, 0, s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, Now, liveCoversIt: false);
        Assert.Equal(ConditioningControlPanel.Services.Startup.Conversion.LapseRow.Closed, reading.Row);
    }

    [Fact]
    public void CloseGrace_PatreonPayingAgainReopens()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        EntitlementTierRule.CloseGrace(s, 0, Now);
        Assert.True(PatreonActiveRule.Restamp(s, new PatreonActiveReading(true, Now, 2), Now));
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonLabValidUntil);
    }

    [Theory]
    [InlineData(2, 0, null, false)]  // the first lower reading can be a late one
    [InlineData(2, 0, 0, true)]      // the second agrees
    [InlineData(2, 0, 1, false)]     // it moved: this value is new, so it counts as a first reading
    [InlineData(2, 1, 1, true)]
    [InlineData(0, 0, 0, false)]     // Same: a sign-in lowered the stored tier, may be a blank twin
    [InlineData(1, 1, 1, false)]
    [InlineData(1, 2, 0, false)]
    public void ConfirmedLower_NeedsTwoAgreeingLowerReadings(int stored, int server, int? seen, bool expected)
        => Assert.Equal(expected, EntitlementTierRule.ConfirmedLower(EntitlementTierRule.Decide(stored, server), server, seen));

    [Fact]
    public void ConfirmedLower_NoReadingIsNotConfirmed()
        => Assert.False(EntitlementTierRule.ConfirmedLower(EntitlementVerdict.NoSignal, null, null));

    [Theory]
    [InlineData(2, 0, true)]     // PayPal or Stripe paid again: stored stayed 2, so the verdict is Same
    [InlineData(1, 0, true)]
    [InlineData(2, 1, true)]     // Prime to Basic and back
    [InlineData(0, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(0, 1, false)]
    [InlineData(3, 0, false)]
    public void ReopensGrace_OnlyAboveTheRememberedLowerReading(int server, int seen, bool expected)
        => Assert.Equal(expected, EntitlementTierRule.ReopensGrace(server, seen));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, null)]        // no remembered drop: a frozen tier never re-stamps itself
    public void ReopensGrace_NeedsBothReadings(int? server, int? seen)
        => Assert.False(EntitlementTierRule.ReopensGrace(server, seen));

    [Fact]
    public void Drop_ThenPayAgain_WalksThroughTheSeenValue()
    {
        var s = new AppSettings { PatreonTier = 2, PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        void Reading(int server)
        {
            var verdict = EntitlementTierRule.Decide(s.PatreonTier, server);
            if (EntitlementTierRule.ConfirmedLower(verdict, server, s.ServerTierSeen)) EntitlementTierRule.CloseGrace(s, server, Now);
            if (verdict != EntitlementVerdict.Rise && EntitlementTierRule.ReopensGrace(server, s.ServerTierSeen)) EntitlementTierRule.ExtendGrace(s, server, Now.AddHours(1));
            s.ServerTierSeen = EntitlementTierRule.ServerTierSeenAfter(verdict, server, s.ServerTierSeen);
        }

        Reading(0);
        Assert.True(s.PatreonPremiumValidUntil > Now);   // one reading changes nothing
        Reading(2);
        Assert.Null(s.ServerTierSeen);                   // a late reading is forgotten
        Assert.Equal(Now.AddHours(1).AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);

        Reading(0);
        Reading(0);
        Assert.Equal(Now, s.PatreonPremiumValidUntil);   // the second agreeing reading closes it
        Assert.Equal(Now, s.PatreonLabValidUntil);
    }

    [Fact]
    public void Celebration_EveryKeyHasACard()
    {
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT1));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT2));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.LegacyKey));
        Assert.Contains("Prime", FeatureIntros.All[TierCelebration.KeyT2].Title);
    }
}
