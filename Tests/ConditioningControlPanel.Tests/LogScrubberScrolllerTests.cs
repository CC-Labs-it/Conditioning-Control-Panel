using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Scrolller items never ride a bug report (licence, 2026-10-21): a feed id
/// ("scrolller/&lt;sub&gt;/&lt;postId&gt;") names a post and a scrolller.com url names its media, so
/// <see cref="LogScrubber"/> swaps both for placeholders and counts them. Anything else, other
/// hosts included, is left as it was.
/// </summary>
public class LogScrubberScrolllerTests
{
    [Fact]
    public void A_feed_id_is_redacted_and_counted()
    {
        var (text, counts) = LogScrubber.Scrub("[FYP] media-error for scrolller/EroticHypnosis/abc-123_x");
        Assert.Equal("[FYP] media-error for scrolller/[redacted]", text);
        Assert.Equal(1, counts.Scrolller);
        Assert.Equal(0, counts.Paths);
    }

    [Theory]
    [InlineData("https://images.scrolller.com/abc/def-1080.jpg")]
    [InlineData("http://v.scrolller.com/q1.mp4?x=1&t=2")]
    [InlineData("https://scrolller.com/r/sub/post-9")]
    [InlineData("HTTPS://API.SCROLLLER.COM/api/v2/graphql")]
    public void A_scrolller_url_becomes_a_placeholder(string url)
    {
        var (text, counts) = LogScrubber.Scrub("fetch " + url + " failed");
        Assert.Equal("fetch [scrolller url] failed", text);
        Assert.Equal(1, counts.Scrolller);
    }

    [Fact]
    public void A_url_inside_json_stops_at_its_quote()
    {
        var (text, counts) = LogScrubber.Scrub("{\"url\":\"https://images.scrolller.com/a.jpg\",\"ok\":true}");
        Assert.Equal("{\"url\":\"[scrolller url]\",\"ok\":true}", text);
        Assert.Equal(1, counts.Scrolller);
    }

    [Fact]
    public void Several_items_are_each_counted()
    {
        var (text, counts) = LogScrubber.Scrub(
            "a scrolller/s/1 b scrolller/s/2 c (https://i.scrolller.com/x.png) d");
        Assert.Equal("a scrolller/[redacted] b scrolller/[redacted] c ([scrolller url]) d", text);
        Assert.Equal(3, counts.Scrolller);
    }

    [Theory]
    [InlineData("https://example.com/scrolller.jpg")]
    [InlineData("https://hypnotube.com/video/1")]
    [InlineData("scrolller is on")]
    [InlineData("notscrolller/x/y")]
    public void Other_text_is_left_alone(string line)
    {
        var (text, counts) = LogScrubber.Scrub(line);
        Assert.Equal(line, text);
        Assert.Equal(0, counts.Scrolller);
    }

    [Fact]
    public void Counts_add_up_across_passes()
    {
        var a = LogScrubber.Scrub("scrolller/s/1").Counts;
        var b = LogScrubber.Scrub("https://i.scrolller.com/1.jpg and C:\\Users\\alice\\x").Counts;
        var sum = a.Add(b);
        Assert.Equal(2, sum.Scrolller);
        Assert.Equal(1, sum.Paths);
        Assert.Equal(0, ScrubberCounts.Empty.Scrolller);
    }
}
