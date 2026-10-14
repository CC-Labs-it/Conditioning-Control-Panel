using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The game-page HTTP relay gate (chess pbp:net, Goon net-post): the host signs these
/// calls with the player's token, so a path that escapes the game's prefix after Uri parsing is
/// an authenticated open proxy. Attack strings must fail, real routes must pass.</summary>
public class HostNetPathRuleTests
{
    private const string Base = "https://codebambi-proxy.vercel.app";
    private const string Pbp = "/v2/pbp/";
    private const string Goon = "/v2/goon/";

    [Theory]
    [InlineData("/v2/pbp/lobby")]
    [InlineData("/v2/pbp/lobby/enter")]
    [InlineData("/v2/pbp/lobby?unified_id=u_123abc")]
    [InlineData("/v2/pbp/match/m_9f2c/events?unified_id=discord%3A1234&after=12")]
    [InlineData("/v2/pbp/match/m_9f2c/move")]
    [InlineData("/v2/pbp/challenge/c_77/accept")]
    [InlineData("/v2/pbp/match/m_1/claim_timeout")]
    [InlineData("/v2/pbp/history?unified_id=u_1&limit=20")]
    [InlineData("/v2/pbp/watch/m_1?since=0")]
    public void RealPbpRoutes_Pass_AndLandOnTheProxy(string path)
    {
        Assert.True(HostNetPathRule.TryBuild(Base, path, Pbp, out var uri));
        Assert.NotNull(uri);
        Assert.Equal("codebambi-proxy.vercel.app", uri!.Host);
        Assert.Equal("https", uri.Scheme);
        Assert.StartsWith(Pbp, uri.AbsolutePath);
        Assert.Equal(Base + path, uri.OriginalString);
    }

    [Theory]
    [InlineData("/v2/goon/signal")]
    [InlineData("/v2/goon/peercard")]
    [InlineData("/v2/goon/list?unified_id=u_1")]
    public void RealGoonRoutes_Pass(string path)
    {
        Assert.True(HostNetPathRule.TryBuild(Base, path, Goon, out var uri));
        Assert.StartsWith(Goon, uri!.AbsolutePath);
    }

    [Theory]
    // Dot segments: System.Uri collapses these to /v2/user/me.
    [InlineData("/v2/pbp/../user/me")]
    [InlineData("/v2/pbp/../../v2/user/me")]
    [InlineData("/v2/pbp/./../user/me")]
    [InlineData("/v2/pbp/lobby/../../user/me")]
    [InlineData("/v2/pbp/..")]
    [InlineData("/v2/pbp/...")]
    // Encoded dots and slashes, either case, and double-encoded.
    [InlineData("/v2/pbp/%2e%2e/user/me")]
    [InlineData("/v2/pbp/%2E%2E/user/me")]
    [InlineData("/v2/pbp/.%2e/user/me")]
    [InlineData("/v2/pbp/%2e./user/me")]
    [InlineData("/v2/pbp/..%2fuser%2fme")]
    [InlineData("/v2/pbp/..%2Fuser%2Fme")]
    [InlineData("/v2/pbp/%252e%252e/user/me")]
    [InlineData("/v2/pbp/lobby%00")]
    // Backslash (.NET reads it as a slash) and its encoding.
    [InlineData("/v2/pbp/..\\user\\me")]
    [InlineData("/v2/pbp\\..\\user\\me")]
    [InlineData("/v2/pbp/..%5cuser%5cme")]
    [InlineData("/v2/pbp/..%5Cuser%5Cme")]
    // Double slash and authority tricks.
    [InlineData("/v2/pbp//evil.example/x")]
    [InlineData("/v2/pbp/x//y")]
    // Absolute URLs and schemes.
    [InlineData("https://evil.example/v2/pbp/lobby")]
    [InlineData("//evil.example/v2/pbp/lobby")]
    [InlineData("/v2/pbp/https://evil.example/")]
    [InlineData("/v2/pbp/lobby?next=https://evil.example/")]
    // Query and fragment tricks.
    [InlineData("/v2/pbp?x=/v2/pbp/")]
    [InlineData("/v2/pbp/lobby?x=/../../user/me")]
    [InlineData("/v2/pbp/lobby#/../../user/me")]
    // Whitespace, control characters, Unicode look-alike dots.
    [InlineData("/v2/pbp/ ../user/me")]
    [InlineData("/v2/pbp/lobby\t")]
    [InlineData("/v2/pbp/lobby\r\nX-Evil: 1")]
    [InlineData("/v2/pbp/\uFF0E\uFF0E/user/me")]
    [InlineData("/v2/pbp/\u2024\u2024/user/me")]
    // Outside the prefix to begin with.
    [InlineData("/v2/user/me")]
    [InlineData("/v2/pbpx/lobby")]
    [InlineData("/V2/PBP/lobby")]
    [InlineData("v2/pbp/lobby")]
    [InlineData("")]
    [InlineData(null)]
    public void AttackStrings_AreRefused(string? path)
    {
        Assert.False(HostNetPathRule.TryBuild(Base, path, Pbp, out var uri));
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("/v2/goon/../user/me")]
    [InlineData("/v2/goon/%2e%2e/user/me")]
    [InlineData("/v2/goon\\..\\user\\me")]
    [InlineData("/v2/pbp/lobby")]
    public void GoonPrefix_RefusesEscapesAndOtherGames(string path)
    {
        Assert.False(HostNetPathRule.TryBuild(Base, path, Goon, out _));
    }

    [Fact]
    public void OverlongPath_IsRefused()
    {
        var path = "/v2/pbp/lobby?x=" + new string('a', HostNetPathRule.MaxPathLength);
        Assert.False(HostNetPathRule.TryBuild(Base, path, Pbp, out _));
    }

    [Fact]
    public void BadBaseOrPrefix_IsRefused()
    {
        Assert.False(HostNetPathRule.TryBuild("not a url", "/v2/pbp/lobby", Pbp, out _));
        Assert.False(HostNetPathRule.TryBuild(Base, "/v2/pbp/lobby", "", out _));
        Assert.False(HostNetPathRule.TryBuild(Base, "/v2/pbp/lobby", "v2/pbp/", out _));
    }

    [Fact]
    public void BaseWithTrailingSlash_StillBuildsOneSlash()
    {
        Assert.True(HostNetPathRule.TryBuild(Base + "/", "/v2/pbp/lobby", Pbp, out var uri));
        Assert.Equal("/v2/pbp/lobby", uri!.AbsolutePath);
    }
}
