using System;
using ConditioningControlPanel.Services.Startup.Conversion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The pass code wire: which text counts as a pass code, and how a redeem reply reads.</summary>
public class CryptoPassApiTests
{
    [Theory]
    [InlineData("CCP-PRIME-3M-7K4Q-X9WD-M2HT-ABCD", "CCP-PRIME-3M-7K4Q-X9WD-M2HT-ABCD")]
    [InlineData("  ccp-basic-12m-7k4q-x9wd-m2ht-abcd ", "CCP-BASIC-12M-7K4Q-X9WD-M2HT-ABCD")]
    [InlineData("CCP PRIME 1M 7K4Q X9WD M2HT ABCD", "CCP-PRIME-1M-7K4Q-X9WD-M2HT-ABCD")]
    [InlineData("ccp-prime-14d-7k4q-x9wd-m2ht-abcd", "CCP-PRIME-14D-7K4Q-X9WD-M2HT-ABCD")]   // a handed-out code counts days
    public void NormalizeCode_AcceptsWhatPeopleType(string typed, string expected)
        => Assert.Equal(expected, CryptoPassApi.NormalizeCode(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SPIRALOCT")]                                   // a weekly post code, not a pass
    [InlineData("CCP-PRIME-0M-7K4Q-X9WD-M2HT-ABCD")]            // no length
    [InlineData("CCP-PRIME-3W-7K4Q-X9WD-M2HT-ABCD")]            // months or days only
    [InlineData("CCP-PRIME-3M-7K4Q-X9WD-M2HT")]                 // one group short
    [InlineData("CCP-PRIME-3M-0O1I-X9WD-M2HT-ABCD")]            // symbols the alphabet leaves out
    [InlineData("CCP-GOLD-3M-7K4Q-X9WD-M2HT-ABCD")]
    public void NormalizeCode_RefusesEverythingElse(string? typed)
        => Assert.Null(CryptoPassApi.NormalizeCode(typed));

    [Fact]
    public void Redeem_Prime_ReadsThePrimeDate()
    {
        var r = CryptoPassApi.ParseRedeem(200,
            "{\"ok\":true,\"tier\":2,\"months\":3,\"pass\":{\"tier\":2,\"prime_until\":\"2027-02-11T12:00:00.000Z\",\"basic_until\":null,\"ends_at\":\"2027-02-11T12:00:00.000Z\",\"days_left\":92}}");
        Assert.True(r.Ok);
        Assert.Equal(2, r.Tier);
        Assert.Equal(new DateTime(2027, 2, 11, 12, 0, 0, DateTimeKind.Utc), r.UntilUtc);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void Redeem_BasicCodeOnAPrimePass_StillReadsPrime()
    {
        // A Basic code bought on top of Prime runs after it: the pass is Prime today.
        var r = CryptoPassApi.ParseRedeem(200,
            "{\"ok\":true,\"tier\":1,\"months\":1,\"pass\":{\"tier\":2,\"prime_until\":\"2026-12-11T12:00:00.000Z\",\"basic_until\":\"2027-01-11T12:00:00.000Z\",\"ends_at\":\"2027-01-11T12:00:00.000Z\",\"days_left\":61}}");
        Assert.True(r.Ok);
        Assert.Equal(2, r.Tier);
        Assert.Equal(new DateTime(2026, 12, 11, 12, 0, 0, DateTimeKind.Utc), r.UntilUtc);
    }

    [Fact]
    public void Redeem_Basic_ReadsTheBasicDate()
    {
        var r = CryptoPassApi.ParseRedeem(200,
            "{\"ok\":true,\"tier\":1,\"months\":1,\"pass\":{\"tier\":1,\"prime_until\":null,\"basic_until\":\"2026-12-11T12:00:00.000Z\",\"ends_at\":\"2026-12-11T12:00:00.000Z\",\"days_left\":30}}");
        Assert.True(r.Ok);
        Assert.Equal(1, r.Tier);
        Assert.Equal(new DateTime(2026, 12, 11, 12, 0, 0, DateTimeKind.Utc), r.UntilUtc);
    }

    [Theory]
    [InlineData(400, "{\"ok\":false,\"error\":\"unknown_code\"}", "unknown_code")]
    [InlineData(409, "{\"ok\":false,\"error\":\"already_redeemed\",\"yours\":true}", "already_redeemed")]
    [InlineData(429, "{\"ok\":false,\"error\":\"too_fast\"}", "too_fast")]
    [InlineData(410, "{\"ok\":false,\"error\":\"expired\"}", "expired")]
    [InlineData(400, "<html>", "unknown_code")]
    [InlineData(503, "{\"ok\":false,\"error\":\"crypto_off\"}", "offline")]
    [InlineData(401, "{\"error\":\"invalid_token\"}", "offline")]
    [InlineData(200, "{\"ok\":true}", "offline")]                 // a yes with no pass is not a yes
    [InlineData(200, "{\"ok\":true,\"pass\":{\"tier\":7,\"ends_at\":\"2027-01-01T00:00:00Z\"}}", "offline")]
    public void Redeem_Refusals(int status, string body, string reason)
    {
        var r = CryptoPassApi.ParseRedeem(status, body);
        Assert.False(r.Ok);
        Assert.Equal(reason, r.Reason);
    }
}
