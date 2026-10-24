using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Host and join rules (owner call 2026-09-24), now in Core: patrons host and send, every
/// signed-in account joins, the opponent's niches are fetched unless online pictures are off, and
/// page-supplied strings are capped before they ride a request.</summary>
[Collection(CoreSecretsStatics.Name)]
public class GoonHostGateTests
{
    [Theory]
    [InlineData(null, true)]    // never set: joining a Scrolller game is the opt-in
    [InlineData(true, true)]
    [InlineData(false, false)]  // switched off for the Goon Game: declined, own pictures stand in
    public void The_peer_pool_follows_the_players_own_online_switch(bool? online, bool fetch)
        => Assert.Equal(fetch, GoonHostRules.PeerFetchAllowed(online));

    [Fact]
    public void Patrons_host_and_send_and_any_signed_in_account_joins()
    {
        var premium = CoreAccount.HasPremiumAccessProvider;
        var id = CoreAccount.UnifiedUserId;
        try
        {
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreAccount.UnifiedUserId = "u_free";
            Assert.False(GoonHostRules.HostingAllowed());
            Assert.False(GoonHostRules.TransferAllowed());
            Assert.True(GoonHostRules.JoiningAllowed());

            CoreAccount.HasPremiumAccessProvider = () => true;
            CoreAccount.UnifiedUserId = null;
            Assert.True(GoonHostRules.HostingAllowed());
            Assert.True(GoonHostRules.TransferAllowed());
            Assert.False(GoonHostRules.JoiningAllowed());

            // A throwing account read is "no", as the WPF try/catch had it.
            CoreAccount.HasPremiumAccessProvider = () => throw new InvalidOperationException();
            Assert.False(GoonHostRules.HostingAllowed());
        }
        finally
        {
            CoreAccount.HasPremiumAccessProvider = premium;
            CoreAccount.UnifiedUserId = id;
        }
    }

    [Theory]
    [InlineData("123456789012345678", true)]
    [InlineData("123456789012345678901", false)]  // 21 digits: longer than any snowflake
    [InlineData("12a4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_snowflake_is_digits_only(string? id, bool ok)
        => Assert.Equal(ok, GoonHostRules.IsSnowflake(id));

    [Theory]
    [InlineData("  abc  ", 8, "abc")]
    [InlineData("abcdefgh", 4, "abcd")]
    [InlineData("   ", 4, "")]
    [InlineData(null, 4, "")]
    public void Page_strings_are_trimmed_and_capped(string? v, int max, string expected)
        => Assert.Equal(expected, GoonHostRules.SafeShort(v, max));
}
