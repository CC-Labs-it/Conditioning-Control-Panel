using System;
using ConditioningControlPanel.Services.Startup.Conversion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The post code wire (brief C6): live reads, redeem replies and the code field.</summary>
public class ConversionPostCodeTests
{
    [Fact]
    public void Live_ReadsTheFourFields()
    {
        var live = PostCodeApi.ParseLive(200, "{\"live\":true,\"feature\":\"takeover\",\"ends_at\":\"2026-10-15T12:00:00Z\",\"redeemed\":false}")!;
        Assert.True(live.Live);
        Assert.Equal("takeover", live.Feature);
        Assert.Equal(new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc), live.EndsUtc);
        Assert.False(live.Redeemed);
    }

    [Theory]
    [InlineData(200, "{\"live\":true,\"feature\":\"nonsense\",\"redeemed\":false}")]
    [InlineData(200, "{\"live\":true,\"feature\":null,\"redeemed\":false}")]
    [InlineData(200, "{\"live\":false,\"feature\":null,\"ends_at\":null,\"redeemed\":false}")]
    public void Live_IsNotLive_WithoutAKnownFeature(int status, string body)
        => Assert.False(PostCodeApi.ParseLive(status, body)!.Live);

    [Theory]
    [InlineData(401, "{}")]
    [InlineData(404, "<html>")]
    [InlineData(200, "not json")]
    [InlineData(200, "")]
    public void Live_UnreadableIsNull(int status, string body) => Assert.Null(PostCodeApi.ParseLive(status, body));

    [Fact]
    public void Redeem_Ok_CarriesFeatureAndUntil()
    {
        var r = PostCodeApi.ParseRedeem(200, "{\"ok\":true,\"feature\":\"dtrh\",\"until\":\"2026-10-15T12:00:00.000Z\"}");
        Assert.True(r.Ok);
        Assert.Equal("dtrh", r.Feature);
        Assert.Equal(new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc), r.UntilUtc);
    }

    [Theory]
    [InlineData(400, "{\"error\":\"unknown_code\"}", "unknown_code")]
    [InlineData(409, "{\"error\":\"already_redeemed\"}", "already_redeemed")]
    [InlineData(410, "{\"error\":\"expired\"}", "expired")]
    [InlineData(400, "", "unknown_code")]
    [InlineData(409, "", "already_redeemed")]
    [InlineData(410, "", "expired")]
    [InlineData(400, "{\"ok\":false,\"reason\":\"expired\"}", "expired")]
    [InlineData(500, "{}", "offline")]
    [InlineData(429, "{}", "offline")]
    public void Redeem_Refusals_ReadAsTheirWord(int status, string body, string reason)
    {
        var r = PostCodeApi.ParseRedeem(status, body);
        Assert.False(r.Ok);
        Assert.Equal(reason, r.Reason);
    }

    [Theory]
    [InlineData("  WEEK42  ", "WEEK42")]
    [InlineData("a", "a")]
    public void Code_IsTrimmed(string input, string expected) => Assert.Equal(expected, PostCodeApi.NormalizeCode(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    public void Code_Refused(string? input) => Assert.Null(PostCodeApi.NormalizeCode(input));

    [Fact]
    public void Code_RefusesOverlong() => Assert.Null(PostCodeApi.NormalizeCode(new string('x', 65)));
}
