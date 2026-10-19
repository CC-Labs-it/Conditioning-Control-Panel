using System.Collections.Generic;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Who goes through the Scrolller device cache (owner, 2026-10-19): RemoteMediaCache's routing,
/// the mandatory-video remote pick (plays only what needs no fresh fetch at play time) and the
/// away rule (lock or display off, never minimise).
/// </summary>
public sealed class ScrolllerCacheRoutingTests
{
    [Theory]
    [InlineData(false, false, false, "Network")]
    [InlineData(false, true, true, "Network")]
    [InlineData(true, true, false, "DiskHit")]   // a hit costs nothing, even when spent
    [InlineData(true, false, true, "FreshThroughDisk")]
    [InlineData(true, false, false, "Refused")]  // spent or away: callers fall back to local
    public void Remote_media_routing(bool scrolller, bool diskHit, bool mayFetch, string expected)
        => Assert.Equal(expected, RemoteMediaCache.RouteFor(scrolller, diskHit, mayFetch).ToString());

    private static string? Pick(List<string> q, HashSet<string> cached, bool mayFetch)
        => VideoService.PickPlayableRemote(q, u => u.StartsWith("s:"), cached.Contains, _ => mayFetch);

    [Fact]
    public void Remote_pick_takes_the_first_cached_clip_and_leaves_the_rest_for_the_downloader()
    {
        var q = new List<string> { "s:1", "s:2", "s:3" };
        Assert.Equal("s:2", Pick(q, new HashSet<string> { "s:2" }, mayFetch: true));
        Assert.Equal(new[] { "s:1", "s:3" }, q);
    }

    [Fact]
    public void Remote_pick_with_nothing_downloaded_yet_is_null_and_keeps_the_buffer()
    {
        var q = new List<string> { "s:1", "s:2" };
        Assert.Null(Pick(q, new HashSet<string>(), mayFetch: true));
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void Remote_pick_when_spent_drops_what_is_not_cached()
    {
        var q = new List<string> { "s:1", "s:2", "s:3" };
        Assert.Equal("s:3", Pick(q, new HashSet<string> { "s:3" }, mayFetch: false));
        Assert.Empty(q);
    }

    [Fact]
    public void Remote_pick_streams_other_providers_as_before()
    {
        var q = new List<string> { "s:1", "https://other/1.mp4" };
        Assert.Equal("https://other/1.mp4", Pick(q, new HashSet<string>(), mayFetch: true));
        Assert.Equal(new[] { "s:1" }, q);
    }

    [Theory]
    [InlineData(false, ScrolllerAwayWatcher.DisplayOn, false)]
    [InlineData(false, ScrolllerAwayWatcher.DisplayDimmed, false)]
    [InlineData(false, ScrolllerAwayWatcher.DisplayOff, true)]
    [InlineData(true, ScrolllerAwayWatcher.DisplayOn, true)]
    public void Away_is_a_locked_session_or_a_display_switched_off(bool locked, int display, bool away)
        => Assert.Equal(away, ScrolllerAwayWatcher.IsAway(locked, display));
}
