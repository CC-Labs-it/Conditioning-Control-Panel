using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class Sync7bActivationTests
{
    [Fact]
    public void FreshConfirmationRenewsEqualTierFromServerTimeOnly()
    {
        var now = DateTime.UtcNow;
        var s = new AppSettings { PatreonTier = 2, PatreonPremiumValidUntil = now.AddDays(-1), PatreonLabValidUntil = now.AddDays(-1) };
        Assert.True(PatreonActiveRule.Restamp(s, new(true, now.AddHours(-1), 2), now));
        Assert.Equal(now.AddHours(-1).AddDays(14), s.PatreonLabValidUntil);
        Assert.False(PatreonActiveRule.Restamp(s, new(true, now.AddDays(-4), 2), now));
        Assert.False(PatreonActiveRule.Restamp(s, new(true, now, 1), now.AddDays(20)));
        s.PatreonPremiumValidUntil = now.AddDays(30);
        PatreonActiveRule.Restamp(s, new(true, now, 1), now);
        Assert.Equal(now.AddDays(30), s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void CheckoutWatchLimitsChecksAndStopsOnDisarm()
    {
        var now = DateTime.UtcNow;
        CheckoutWatch.Disarm();
        CheckoutWatch.NoteOpened("https://app.cclabs.app/subscribe?plan=prime", now);
        Assert.True(CheckoutWatch.TakeCheck(now));
        Assert.False(CheckoutWatch.TakeCheck(now.AddSeconds(19)));
        Assert.True(CheckoutWatch.TakeCheck(now.AddSeconds(20)));
        Assert.False(CheckoutWatch.TakeCheck(now.AddMinutes(31)));
        CheckoutWatch.Disarm();
        Assert.False(CheckoutWatch.TakeCheck(now.AddSeconds(40)));
        Assert.False(CheckoutWatch.IsCheckoutUrl("https://patreon.com.evil.test/checkout"));
        Assert.False(CheckoutWatch.IsCheckoutUrl("http://patreon.com/checkout"));
    }

    [Theory]
    [InlineData(false, false, 0, 0, JustSubscribedOutcome.SignIn)]
    [InlineData(true, false, 0, 0, JustSubscribedOutcome.Unreachable)]
    [InlineData(true, true, 0, 0, JustSubscribedOutcome.NotYet)]
    [InlineData(true, true, 0, 1, JustSubscribedOutcome.BasicOn)]
    [InlineData(true, true, 1, 2, JustSubscribedOutcome.PrimeOn)]
    public void CheckFeedback(bool signed, bool reached, int before, int after, JustSubscribedOutcome expected) =>
        Assert.Equal(expected, JustSubscribedRule.Decide(signed, reached, before, after));

    [Fact]
    public void NoticeRecordsOnlyConfirmedDropsAndLaterRises()
    {
        Assert.False(TierOnNotice.Decide(null, 2, false).Owed);
        Assert.Equal(2, TierOnNotice.Decide(2, 0, false).Record);
        Assert.Equal(0, TierOnNotice.Decide(2, 0, true).Record);
        Assert.True(TierOnNotice.Decide(0, 2, true).Owed);
        Assert.False(TierOnNotice.Decide(2, 2, true).Owed);
        Assert.Equal("tier_on_prime", TierOnNotice.TitleKey(2));
    }

    [Fact]
    public void StayCapKeepsFortyDespiteSlider() => Assert.Equal(40, FlashStayRule.OnScreen(40, 1, stay: true));
}
