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

    private static readonly DateTime Live = Now.AddDays(9);

    private static GraceStep Step(int? server, DateTime? premium, DateTime? lab, int? closed = null, bool linked = true,
        bool held = false, int? pending = null, DateTime? since = null, DateTime? at = null)
        => EntitlementTierRule.StepGrace(server, premium, lab, closed, linked, held, pending, since, at ?? Now);

    [Fact]
    public void StepGrace_ClosesOnALaunchZeroedInstallToo()
    {
        // No stored tier in the signature: a tokenless launch zeroes it, and the stamps still close.
        var first = Step(0, Live, Live);
        Assert.False(first.Close);
        Assert.Equal(0, first.PendingTier);
        var second = Step(0, Live, Live, pending: first.PendingTier, since: first.PendingSinceUtc, at: Now.AddMinutes(2));
        Assert.True(second.Close);
        Assert.Equal(2, second.ClosedTier);
    }

    [Fact]
    public void StepGrace_TheSecondReadingMustBeTheGapLater()
    {
        var near = Step(0, Live, Live, pending: 0, since: Now, at: Now + EntitlementTierRule.GraceConfirmGap - TimeSpan.FromSeconds(1));
        Assert.False(near.Close);
        Assert.Equal(Now, near.PendingSinceUtc);    // the first reading's time stands
        Assert.True(Step(0, Live, Live, pending: 0, since: Now, at: Now + EntitlementTierRule.GraceConfirmGap).Close);
    }

    [Fact]
    public void StepGrace_ADifferentReadingStartsOver()
    {
        var moved = Step(0, Live, Live, pending: 1, since: Now.AddHours(-1));
        Assert.False(moved.Close);
        Assert.Equal(0, moved.PendingTier);
        Assert.Equal(Now, moved.PendingSinceUtc);
    }

    [Fact]
    public void StepGrace_AnAgreeingReadingForgetsTheDrop()
    {
        var back = Step(2, Live, Live, pending: 0, since: Now.AddHours(-1));
        Assert.False(back.Close);
        Assert.Null(back.PendingTier);
        Assert.Null(back.PendingSinceUtc);
    }

    [Fact]
    public void StepGrace_ABlankTwinNeverCloses()
        => Assert.False(Step(0, Live, Live, linked: false, pending: 0, since: Now.AddHours(-1)).Close);

    [Fact]
    public void StepGrace_ASessionOrGameHoldsTheCloseAndKeepsThePending()
    {
        var held = Step(0, Live, Live, held: true, pending: 0, since: Now.AddHours(-1));
        Assert.False(held.Close);
        Assert.Equal(0, held.PendingTier);
        Assert.True(Step(0, Live, Live, pending: held.PendingTier, since: held.PendingSinceUtc).Close);
    }

    [Fact]
    public void StepGrace_BasicClosesOnlyAPrimeWindow()
    {
        var step = Step(1, Live, Live, pending: 1, since: Now.AddHours(-1));
        Assert.True(step.Close);
        Assert.Equal(2, step.ClosedTier);
        Assert.False(Step(1, Live, null, pending: 1, since: Now.AddHours(-1)).Close);
    }

    [Fact]
    public void StepGrace_ClosedTierNamesThePlanThatWasLive()
        => Assert.Equal(1, Step(0, Live, null, pending: 0, since: Now.AddHours(-1)).ClosedTier);

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(-1)]
    public void StepGrace_NoReadingKeepsThePending(int? server)
    {
        var step = Step(server, Live, Live, pending: 0, since: Now);
        Assert.False(step.Close);
        Assert.Equal(0, step.PendingTier);
        Assert.Equal(Now, step.PendingSinceUtc);
    }

    [Fact]
    public void StepGrace_AnExpiredStampHasNothingToClose()
    {
        var past = Now.AddDays(-1);
        Assert.False(Step(0, past, past, pending: 0, since: Now.AddHours(-1)).Close);
    }

    [Fact]
    public void StepGrace_ReopensOnlyAWindowThatClosed()
    {
        var past = Now.AddDays(-1);
        Assert.True(Step(2, past, past, closed: 2).Reopen);      // PayPal or Stripe paid again
        Assert.True(Step(1, past, past, closed: 1).Reopen);
        Assert.False(Step(2, past, past).Reopen);                // no fanfare for one that never closed
        Assert.False(Step(0, past, past, closed: 2).Reopen);
        Assert.False(Step(2, Live, Live, closed: 2).Reopen);     // already covered
        Assert.False(Step(1, Live, past, closed: 2).Reopen);     // Prime to Basic: Basic was never shut
    }

    [Fact]
    public void StepGrace_ClearsTheClosedTierOnlyAtThatTier()
    {
        Assert.True(Step(2, Live, Live, closed: 2).ClearClosedTier);
        Assert.True(Step(1, Live, null, closed: 1).ClearClosedTier);
        Assert.False(Step(1, Live, null, closed: 2).ClearClosedTier);
        Assert.False(Step(0, null, null, closed: 1).ClearClosedTier);
    }

    [Fact]
    public void ClosedWindow_OwesTheRowAfterALaunchZeroedTheStoredTier()
    {
        var s = new AppSettings { PatreonTier = 0, ServerTierSeen = null, GraceClosedTier = 2, PatreonPremiumValidUntil = Now, PatreonLabValidUntil = Now };
        var reading = ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.DecideLapseFor(s, Now, liveCoversIt: false);
        Assert.Equal(ConditioningControlPanel.Services.Startup.Conversion.LapseRow.Closed, reading.Row);
        Assert.Equal(2, reading.Tier);
    }

    [Fact]
    public void NoClosedWindow_KeepsTheOldLapseRule()
    {
        var s = new AppSettings { PatreonTier = 0, ServerTierSeen = null, PatreonPremiumValidUntil = Now.AddDays(-1) };
        var reading = ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.DecideLapseFor(s, Now, liveCoversIt: false);
        Assert.Equal(ConditioningControlPanel.Services.Startup.Conversion.LapseRow.None, reading.Row);
    }

    [Fact]
    public void GraceClosedTier_NeverRidesTheCloudBackup()
        => Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.GraceClosedTier)));

    [Fact]
    public void Celebration_EveryKeyHasACard()
    {
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT1));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT2));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.LegacyKey));
        Assert.Contains("Prime", FeatureIntros.All[TierCelebration.KeyT2].Title);
    }
}
