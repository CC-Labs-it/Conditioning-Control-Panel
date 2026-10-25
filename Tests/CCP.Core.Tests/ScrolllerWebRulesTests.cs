using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The web gate's rules, moved from WPF ScrolllerWebGate into Core (main c45d58a65, f7102be55,
/// 92fdebaa4) so both heads read one copy. The WPF suite (ScrolllerWebGateTests, ...RouteTests,
/// ScrolllerGateChargeTests) keeps covering them through WPF's forwarders on Windows; these run on Linux.
/// </summary>
public class ScrolllerWebRulesTests
{
    [Theory]
    [InlineData("https://api.scrolller.com/api/v2/graphql", true, "Rewrite")]
    [InlineData("https://api.scrolller.com/api/v2/graphql", false, "Block")]
    [InlineData("wss://scrolller.com./socket", false, "Block")]
    [InlineData("https://notscrolller.com/x", false, "Pass")]
    [InlineData("https://scrolller.com.evil.net/", false, "Pass")]
    [InlineData("https://example.com/?u=https://scrolller.com/", false, "Pass")]
    [InlineData("data:text/plain,scrolller.com", false, "Pass")]
    [InlineData(null, false, "Pass")]
    public void Decides_block_rewrite_or_pass(string? uri, bool enabled, string expected)
        => Assert.Equal(expected, ScrolllerWebRules.Decide(uri, enabled).ToString());

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://app.cclabs.app/race", true)]
    [InlineData("https://ccp.game/", true)]
    [InlineData("https://leash-video.ccp/", true)]
    [InlineData("https://www.scrolller.com/r/x", false)]
    [InlineData("https://notcclabs.app/", false)]
    [InlineData("garbage", false)]
    public void Keeps_only_our_own_referer(string? referer, bool ours)
        => Assert.Equal(ours, ScrolllerWebRules.IsOurReferer(referer));

    [Theory]
    [InlineData("https://images.scrolller.com/a/b.jpg", "GET", false, true, true, false, "Block")]
    [InlineData("https://v.scrolller.com/a/b.mp4", "GET", true, true, false, true, "ServeCached")]
    [InlineData("https://images.scrolller.com/a/b.jpg", "GET", true, false, false, false, "Refuse")]
    [InlineData("https://v.scrolller.com/a/b.mp4", "GET", true, false, true, false, "PassCharged")]
    [InlineData("https://images.scrolller.com/a/b.gif", "GET", true, false, true, false, "FetchWhole")]
    [InlineData("https://api.scrolller.com/api/v2/graphql", "POST", true, false, false, false, "Rewrite")]
    public void Routes_by_cache_share_and_kind(string url, string method, bool enabled, bool cached, bool fresh,
        bool range, string expected)
        => Assert.Equal(expected, ScrolllerWebRules.Route(url, method, enabled, cached, fresh, range,
            ScrolllerWebRules.KindOf(url)).ToString());

    [Theory]
    [InlineData(0, 1000, 5000L, 1000)]
    [InlineData(5000, 5000, 5000L, 0)]      // a cached replay pays nothing
    [InlineData(4500, 1000, 5000L, 500)]    // capped at the whole size
    [InlineData(0, 800, null, 800)]         // no known size: the first response pays
    [InlineData(800, 800, null, 0)]         // and only the first
    public void A_url_never_pays_past_its_size_in_a_day(long already, long bytes, long? total, long expected)
        => Assert.Equal(expected, ScrolllerWebRules.DedupedCharge(already, bytes, total));

    [Theory]
    [InlineData(206, null, "bytes 0-1023/52000", 52000L)]
    [InlineData(206, "1024", "bytes 0-1023/*", null)]
    [InlineData(200, "52000", null, 52000L)]
    public void Reads_the_whole_size(int status, string? length, string? range, long? expected)
        => Assert.Equal(expected, ScrolllerWebRules.TotalSize(status, length, range));

    [Fact]
    public void An_open_range_gets_one_slice()
    {
        Assert.True(ScrolllerWebRules.TryParseRange("bytes=100-", 10_000_000, out var r, out var bad));
        Assert.False(bad);
        Assert.Equal(new ScrolllerByteRange(100, 100 + ScrolllerWebRules.SliceBytes - 1), r);
        Assert.True(ScrolllerWebRules.TryParseRange("bytes=20000000-", 10_000_000, out _, out bad));
        Assert.True(bad);
    }
}
