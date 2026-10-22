using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Desktop side of the Oct 17 Patreon fix: a fresh server confirmation keeps the grace alive, a
/// checkout link arms a short re-check window, the "I just subscribed" line, and the small
/// "Your Prime is on" card owed once per rise.
/// </summary>
public class PatreonActivationTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ 1. grace re-stamp

    [Fact]
    public void Parse_ReadsBothFields()
    {
        var r = PatreonActiveRule.Parse(JObject.Parse(
            "{\"patreon_is_active\":true,\"patreon_confirmed_at\":\"2026-10-17T09:00:00Z\",\"patreon_tier\":2}"));
        Assert.NotNull(r);
        Assert.True(r!.IsActive);
        Assert.Equal(new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc), r.ConfirmedAtUtc);
        Assert.Equal(2, r.PatreonTier);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"patreon_is_active\":\"yes\"}")]
    public void Parse_OlderServerIsNoReading(string body)
        => Assert.Null(PatreonActiveRule.Parse(JObject.Parse(body)));

    [Fact]
    public void Restamp_FreshConfirmation_GraceRunsFromTheConfirmation()
    {
        var s = new AppSettings();
        var at = Now.AddHours(-10);
        Assert.True(PatreonActiveRule.Restamp(s, new PatreonActiveReading(true, at, 1), Now));
        Assert.Equal(at.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);
        Assert.Null(s.PatreonLabValidUntil);
    }

    [Fact]
    public void Restamp_Tier2_StampsLabToo()
    {
        var s = new AppSettings();
        Assert.True(PatreonActiveRule.Restamp(s, new PatreonActiveReading(true, Now.AddHours(-1), 2), Now));
        Assert.NotNull(s.PatreonLabValidUntil);
    }

    [Theory]
    [InlineData(false, -1, 1)]   // not active
    [InlineData(true, -72, 1)]   // exactly 72 h old: stale
    [InlineData(true, -200, 1)]  // long stale
    [InlineData(true, -1, 0)]    // active but no paid tier
    [InlineData(true, 2, 1)]     // two hours in the future: not believed
    public void Restamp_NeverFromAStaleOrOddReading(bool active, int hoursAgo, int tier)
    {
        var s = new AppSettings();
        Assert.False(PatreonActiveRule.Restamp(s, new PatreonActiveReading(active, Now.AddHours(hoursAgo), tier), Now));
        Assert.Null(s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void Restamp_NoConfirmationStamp_DoesNothing()
    {
        var s = new AppSettings();
        Assert.False(PatreonActiveRule.Restamp(s, new PatreonActiveReading(true, null, 1), Now));
        Assert.False(PatreonActiveRule.Restamp(s, null, Now));
    }

    [Fact]
    public void Restamp_NeverShortensALongerWindow()
    {
        var longer = Now.AddDays(30);
        var s = new AppSettings { PatreonPremiumValidUntil = longer };
        Assert.False(PatreonActiveRule.Restamp(s, new PatreonActiveReading(true, Now.AddHours(-2), 1), Now));
        Assert.Equal(longer, s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void Restamp_SmallClockSkewAnchorsAtNow()
    {
        var s = new AppSettings();
        Assert.True(PatreonActiveRule.Restamp(s, new PatreonActiveReading(true, Now.AddMinutes(5), 1), Now));
        Assert.Equal(Now.AddDays(EntitlementTierRule.GraceDays), s.PatreonPremiumValidUntil);
    }

    // ------------------------------------------------------------------ 2. checkout watch

    [Theory]
    [InlineData("https://www.patreon.com/checkout/CodeBambi?rid=27681505", true)]
    [InlineData("https://patreon.com/checkout/CodeBambi?rid=27604786", true)]
    [InlineData("https://app.cclabs.app/subscribe?plan=prime&from=panel", true)]
    [InlineData("https://www.patreon.com/CodeBambi", false)]
    [InlineData("http://www.patreon.com/checkout/CodeBambi", false)]
    [InlineData("https://evil.example/checkout/x", false)]
    [InlineData("https://subscribestar.adult/cc-labs", false)]
    [InlineData(null, false)]
    public void IsCheckoutUrl(string? url, bool expected)
        => Assert.Equal(expected, CheckoutWatch.IsCheckoutUrl(url));

    [Fact]
    public void ShouldCheck_OnlyInsideTheWindowAndTheGap()
    {
        var armed = Now;
        Assert.False(CheckoutWatch.ShouldCheck(null, DateTime.MinValue, Now));
        Assert.True(CheckoutWatch.ShouldCheck(armed, DateTime.MinValue, Now.AddMinutes(1)));
        Assert.False(CheckoutWatch.ShouldCheck(armed, Now.AddMinutes(1), Now.AddMinutes(1).AddSeconds(19)));
        Assert.True(CheckoutWatch.ShouldCheck(armed, Now.AddMinutes(1), Now.AddMinutes(1).AddSeconds(20)));
        Assert.True(CheckoutWatch.ShouldCheck(armed, DateTime.MinValue, Now.AddMinutes(30)));
        Assert.False(CheckoutWatch.ShouldCheck(armed, DateTime.MinValue, Now.AddMinutes(31)));
    }

    [Fact]
    public void TakeCheck_ArmedByACheckoutOnly_SpendsTheSlot()
    {
        CheckoutWatch.Disarm();
        try
        {
            CheckoutWatch.NoteOpened("https://www.patreon.com/CodeBambi", Now);
            Assert.False(CheckoutWatch.TakeCheck(Now.AddSeconds(30)));
            CheckoutWatch.NoteOpened("https://www.patreon.com/checkout/CodeBambi?rid=27681505", Now);
            Assert.True(CheckoutWatch.TakeCheck(Now.AddSeconds(30)));
            Assert.False(CheckoutWatch.TakeCheck(Now.AddSeconds(40)));
            Assert.True(CheckoutWatch.TakeCheck(Now.AddSeconds(50)));
            CheckoutWatch.Disarm();
            Assert.False(CheckoutWatch.TakeCheck(Now.AddMinutes(2)));
        }
        finally { CheckoutWatch.Disarm(); }
    }

    // ------------------------------------------------------------------ 3. "I just subscribed"

    [Theory]
    [InlineData(true, true, 0, 2, JustSubscribedOutcome.PrimeOn)]
    [InlineData(true, true, 1, 2, JustSubscribedOutcome.PrimeOn)]
    [InlineData(true, true, 0, 1, JustSubscribedOutcome.BasicOn)]
    [InlineData(true, true, 0, 0, JustSubscribedOutcome.NotYet)]
    [InlineData(true, true, 1, 1, JustSubscribedOutcome.NotYet)]
    [InlineData(true, false, 0, 0, JustSubscribedOutcome.Unreachable)]
    [InlineData(false, false, 0, 0, JustSubscribedOutcome.SignIn)]
    public void JustSubscribed_SaysWhatItFound(bool signedIn, bool reached, int before, int after, JustSubscribedOutcome expected)
        => Assert.Equal(expected, JustSubscribedRule.Decide(signedIn, reached, before, after));

    [Fact]
    public void JustSubscribed_EveryOutcomeHasAKey()
    {
        foreach (JustSubscribedOutcome o in Enum.GetValues(typeof(JustSubscribedOutcome)))
            Assert.StartsWith("plans_just_subscribed_", JustSubscribedRule.LocKey(o));
    }

    // ------------------------------------------------------------------ 4. "Your Prime is on"

    [Fact]
    public void TierOn_FirstReadingOnlyRecords()
    {
        var v = TierOnNotice.Decide(null, 2, mayRecordDrop: false);
        Assert.False(v.Owed);
        Assert.Equal(2, v.Record);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    public void TierOn_ARiseIsOwedOnce(int last, int current)
    {
        var v = TierOnNotice.Decide(last, current, mayRecordDrop: false);
        Assert.True(v.Owed);
        Assert.Equal(current, v.Record);
        Assert.False(TierOnNotice.Decide(v.Record, current, mayRecordDrop: true).Owed);
    }

    [Fact]
    public void TierOn_DropRecordedOnlyOnceTheServerAnswered()
    {
        Assert.Equal(2, TierOnNotice.Decide(2, 0, mayRecordDrop: false).Record);   // stale cache at launch
        var drop = TierOnNotice.Decide(2, 0, mayRecordDrop: true);
        Assert.False(drop.Owed);
        Assert.Equal(0, drop.Record);
        Assert.True(TierOnNotice.Decide(drop.Record, 2, mayRecordDrop: true).Owed);  // lapsed, then back
    }

    [Fact]
    public void TierOn_KeysPerTier()
    {
        Assert.Equal("tier_on_prime", TierOnNotice.TitleKey(2));
        Assert.Equal("tier_on_basic", TierOnNotice.TitleKey(1));
    }
}
