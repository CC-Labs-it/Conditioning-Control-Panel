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
        EntitlementTierRule.CloseGrace(s, 0, Now);
        Assert.Equal(Now, s.PatreonPremiumValidUntil);
        Assert.Equal(Now, s.PatreonLabValidUntil);
    }

    [Fact]
    public void CloseGrace_BasicKeepsPremiumEndsLab()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        EntitlementTierRule.CloseGrace(s, 1, Now);
        Assert.Equal(Now.AddDays(9), s.PatreonPremiumValidUntil);
        Assert.Equal(Now, s.PatreonLabValidUntil);
    }

    [Fact]
    public void CloseGrace_PrimeIncludingWhitelistEndsNothing()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddHours(25), PatreonLabValidUntil = Now.AddDays(9) };
        EntitlementTierRule.CloseGrace(s, 2, Now);
        Assert.Equal(Now.AddHours(25), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddDays(9), s.PatreonLabValidUntil);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(-1)]
    public void ApplyReading_NoReadingKeepsTheOfflineWindow(int? server)
    {
        var s = new AppSettings { PatreonTier = 2, PatreonPremiumValidUntil = Now.AddDays(9), PatreonLabValidUntil = Now.AddDays(9) };
        Assert.False(EntitlementTierRule.ApplyReading(s, EntitlementVerdict.NoSignal, server, null, () => false, Now).Closed);
        Assert.Equal(Now.AddDays(9), s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void CloseGrace_AnEndedStampKeepsItsDay()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(-3) };
        EntitlementTierRule.CloseGrace(s, 0, Now);
        Assert.Equal(Now.AddDays(-3), s.PatreonPremiumValidUntil);
    }

    private static readonly DateTime Live = Now.AddDays(9);
    private static readonly TimeSpan Gap = EntitlementTierRule.GraceConfirmGap;

    /// <summary>The whole reading, as EntitlementTierSync.ApplyOnUi runs it.</summary>
    private static GraceOutcome Read(AppSettings s, int? server, GracePending? pending = null, bool held = false, DateTime? at = null)
        => EntitlementTierRule.ApplyReading(s, EntitlementTierRule.Decide(s.PatreonTier, server), server, pending, () => held, at ?? Now);

    private static AppSettings Prime() => new() { PatreonTier = 2, PatreonPremiumValidUntil = Live, PatreonLabValidUntil = Live };

    [Fact]
    public void ApplyReading_OneLowerReadingChangesNothing()
    {
        var s = Prime();
        var out1 = Read(s, 0);
        Assert.False(out1.Closed);
        Assert.False(out1.Dirty);
        Assert.Equal(new GracePending(0, Now), out1.Pending);
        Assert.Equal(Live, s.PatreonLabValidUntil);
        Assert.Equal(2, s.PatreonTier);
    }

    [Fact]
    public void ApplyReading_TheSameReadingAfterTheGapCloses()
    {
        var s = Prime();
        var first = Read(s, 0);
        var second = Read(s, 0, first.Pending, at: Now + Gap);
        Assert.True(second.Closed);
        Assert.True(second.Dirty);
        Assert.Null(second.Pending);
        Assert.Equal(Now + Gap, s.PatreonPremiumValidUntil);
        Assert.Equal(Now + Gap, s.PatreonLabValidUntil);
        Assert.Equal(0, s.PatreonTier);
        Assert.Equal(2, s.GraceClosedTier);
    }

    [Fact]
    public void ApplyReading_TwoReadingsInsideTheGapAreNotConfirmation()
    {
        var s = Prime();
        var first = Read(s, 0);
        var near = Read(s, 0, first.Pending, at: Now + Gap - TimeSpan.FromSeconds(1));
        Assert.False(near.Closed);
        Assert.Equal(Now, near.Pending!.SinceUtc);   // the first reading's time stands
    }

    [Fact]
    public void ApplyReading_ADifferentReadingStartsOver()
    {
        var s = Prime();
        var moved = Read(s, 0, new GracePending(1, Now.AddHours(-1)));
        Assert.False(moved.Closed);
        Assert.Equal(new GracePending(0, Now), moved.Pending);
    }

    [Fact]
    public void ApplyReading_AnAgreeingReadingForgetsALateOne()
    {
        var s = Prime();
        var back = Read(s, 2, new GracePending(0, Now.AddHours(-1)));
        Assert.False(back.Closed);
        Assert.Null(back.Pending);
        Assert.Equal(2, s.PatreonTier);
        Assert.True(s.PatreonLabValidUntil >= Live);   // never shortened, and a paying reading renews
    }

    [Fact]
    public void ApplyReading_ASessionOrGameHoldsTheClose()
    {
        var s = Prime();
        var waiting = new GracePending(0, Now.AddHours(-1));
        var held = Read(s, 0, waiting, held: true);
        Assert.False(held.Closed);
        Assert.Equal(waiting, held.Pending);
        Assert.Equal(Live, s.PatreonLabValidUntil);
        Assert.True(Read(s, 0, held.Pending).Closed);
    }

    [Fact]
    public void ApplyReading_ABlankTwinAfterASignInNeverCloses()
    {
        // ApplyUserDataToSettings already lowered the stored tier: Same, whatever the stamps say.
        var s = new AppSettings { PatreonTier = 0, PatreonPremiumValidUntil = Live, PatreonLabValidUntil = Live };
        var r = Read(s, 0, new GracePending(0, Now.AddHours(-1)));
        Assert.False(r.Closed);
        Assert.Equal(Live, s.PatreonLabValidUntil);
        Assert.Null(s.GraceClosedTier);
    }

    [Fact]
    public void ApplyReading_PrimeToBasicClosesOnlyPrimeAndKeepsBasicRenewing()
    {
        var s = Prime();
        var r = Read(s, 1, new GracePending(1, Now.AddHours(-1)));
        Assert.True(r.Closed);
        Assert.Equal(Live, s.PatreonPremiumValidUntil);
        Assert.Equal(Now, s.PatreonLabValidUntil);
        Assert.Equal(1, s.PatreonTier);
        Assert.Equal(2, s.GraceClosedTier);

        // Fourteen days on, still Basic: renewed quietly, a Same verdict with no rise to announce.
        var later = Now.AddDays(13);
        var renewed = Read(s, 1, at: later);
        Assert.Equal(EntitlementVerdict.Same, EntitlementTierRule.Decide(s.PatreonTier, 1));
        Assert.True(renewed.Dirty);
        Assert.Equal(later.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);
        Assert.Equal(2, s.GraceClosedTier);
    }

    [Fact]
    public void ApplyReading_PayingAgainIsAnOrdinaryRise()
    {
        var s = Prime();
        Read(s, 0, new GracePending(0, Now.AddHours(-1)));
        Assert.Equal(EntitlementVerdict.Rise, EntitlementTierRule.Decide(s.PatreonTier, 2));
        var back = Read(s, 2);
        Assert.True(back.Dirty);
        Assert.Null(s.GraceClosedTier);
    }

    [Fact]
    public void ApplyReading_APayerWithAKeptStoredTierStaysRenewed()
    {
        // A signed-in launch no longer zeroes the tier: Same verdict, and the stamps must still move.
        var s = new AppSettings { PatreonTier = 2, PatreonPremiumValidUntil = Now.AddDays(2), PatreonLabValidUntil = Now.AddDays(2) };
        Assert.True(Read(s, 2).Dirty);
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonLabValidUntil);
    }

    [Fact]
    public void RenewGrace_DoesNotSaveEveryHeartbeat()
    {
        var s = new AppSettings { PatreonTier = 2 };
        Assert.True(EntitlementTierRule.RenewGrace(s, 2, Now));
        Assert.False(EntitlementTierRule.RenewGrace(s, 2, Now.AddMinutes(2)));
        Assert.False(EntitlementTierRule.RenewGrace(s, 2, Now.AddHours(23)));
        Assert.True(EntitlementTierRule.RenewGrace(s, 2, Now.AddHours(25)));
    }

    [Fact]
    public void RenewGrace_TierZeroRenewsNothing()
    {
        var s = new AppSettings();
        Assert.False(EntitlementTierRule.RenewGrace(s, 0, Now));
        Assert.Null(s.PatreonPremiumValidUntil);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(-1)]
    public void ApplyReading_NoReadingKeepsEverything(int? server)
    {
        var s = Prime();
        var waiting = new GracePending(0, Now);
        var r = Read(s, server, waiting);
        Assert.False(r.Closed);
        Assert.False(r.Dirty);
        Assert.Equal(waiting, r.Pending);
        Assert.Equal(Live, s.PatreonLabValidUntil);
    }

    [Fact]
    public void ApplyReading_AHeldSessionNeverRunsTheProbeOnAnOrdinaryReading()
    {
        var asked = false;
        EntitlementTierRule.ApplyReading(Prime(), EntitlementVerdict.Same, 2, null, () => { asked = true; return true; }, Now);
        EntitlementTierRule.ApplyReading(Prime(), EntitlementVerdict.Lower, 0, null, () => { asked = true; return true; }, Now);
        Assert.False(asked);
    }

    [Fact]
    public void ClosedWindow_OwesTheRowAfterALaunchZeroedTheStoredTier()
    {
        var s = new AppSettings { PatreonTier = 0, ServerTierSeen = null, GraceClosedTier = 2, GraceClosedAtUtc = Now, PatreonPremiumValidUntil = Now, PatreonLabValidUntil = Now };
        var reading = ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.DecideLapseFor(s, Now);
        Assert.Equal(ConditioningControlPanel.Services.Startup.Conversion.LapseRow.Closed, reading.Row);
        Assert.Equal(2, reading.Tier);
    }

    [Fact]
    public void NoClosedWindow_KeepsTheOldLapseRule()
    {
        var s = new AppSettings { PatreonTier = 0, ServerTierSeen = null, PatreonPremiumValidUntil = Now.AddDays(-1) };
        var reading = ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.DecideLapseFor(s, Now);
        Assert.Equal(ConditioningControlPanel.Services.Startup.Conversion.LapseRow.None, reading.Row);
    }

    [Fact]
    public void ClosedWindow_PrimeToBasicWithNoLabStampStillPostsTheRow()
    {
        // A Basic validate nulls the Lab stamp, and the kept Basic window is 14 days out.
        var s = new AppSettings { PatreonTier = 1, GraceClosedTier = 2, GraceClosedAtUtc = Now, PatreonPremiumValidUntil = Now.AddDays(14), PatreonLabValidUntil = null };
        var reading = ConditioningControlPanel.Services.Startup.Conversion.ConversionRules.DecideLapseFor(s, Now);
        Assert.Equal(ConditioningControlPanel.Services.Startup.Conversion.LapseRow.Closed, reading.Row);
        Assert.Equal(2, reading.Tier);
        Assert.Equal(Now, reading.UntilUtc);
    }

    [Theory]
    [InlineData(29, 2)]
    [InlineData(31, null)]
    public void ClosedTierNow_EndsWithTheRowWindow(int daysAgo, int? expected)
    {
        var s = new AppSettings { PatreonTier = 0, GraceClosedTier = 2, GraceClosedAtUtc = Now.AddDays(-daysAgo) };
        Assert.Equal(expected, EntitlementTierRule.ClosedTierNow(s, Now));
    }

    [Fact]
    public void ClosedTierNow_NoTimeMeansNoClose()
        => Assert.Null(EntitlementTierRule.ClosedTierNow(new AppSettings { GraceClosedTier = 2 }, Now));

    [Fact]
    public void ApplyReading_ForgetsACloseWhoseWindowEnded()
    {
        var s = new AppSettings { PatreonTier = 1, GraceClosedTier = 2, GraceClosedAtUtc = Now.AddDays(-31), PatreonPremiumValidUntil = Live };
        Assert.True(Read(s, 1).Dirty);
        Assert.Null(s.GraceClosedTier);
        Assert.Null(s.GraceClosedAtUtc);
    }

    [Fact]
    public void ApplyReading_ACloseRemembersItsDay()
    {
        var s = Prime();
        Read(s, 0, new GracePending(0, Now.AddHours(-1)));
        Assert.Equal(Now, s.GraceClosedAtUtc);
    }

    [Fact]
    public void ExtendPremiumTo_NeverShortensAReadingsStamp()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(14) };
        EntitlementTierRule.ExtendPremiumTo(s, Now.AddHours(25));
        Assert.Equal(Now.AddDays(14), s.PatreonPremiumValidUntil);
        Assert.False(EntitlementTierRule.RenewGrace(s, 1, Now));

        var empty = new AppSettings();
        EntitlementTierRule.ExtendPremiumTo(empty, Now.AddHours(25));
        Assert.Equal(Now.AddHours(25), empty.PatreonPremiumValidUntil);
    }

    [Fact]
    public void GraceClosedTier_NeverRidesTheCloudBackup()
        => Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.GraceClosedTier))
                       && ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.GraceClosedAtUtc)));

    [Fact]
    public void Celebration_EveryKeyHasACard()
    {
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT1));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.KeyT2));
        Assert.True(FeatureIntros.All.ContainsKey(TierCelebration.LegacyKey));
        Assert.Contains("Prime", FeatureIntros.All[TierCelebration.KeyT2].Title);
    }
}
