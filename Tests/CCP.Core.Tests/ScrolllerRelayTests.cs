using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Avalonia head's page gate (Core ScrolllerRelay): every Scrolller media url a host posts becomes a
/// loopback relay path, the relay maps back only onto Scrolller media (never an open proxy), and the route rule
/// matches WPF ScrolllerWebGate.Route (switch off blocks, cached is free, fresh only when allowed).</summary>
public class ScrolllerRelayTests
{
    [Theory]
    [InlineData("https://images.scrolller.com/abc/clip-1.mp4", "/scrolller-media/images/abc/clip-1.mp4")]
    [InlineData("https://IMAGES.scrolller.com/a.JPG?x=1", "/scrolller-media/images/a.JPG?x=1")]
    [InlineData("http://images.scrolller.com/a.webp", "/scrolller-media/images/a.webp")]
    public void Media_urls_become_relay_paths(string url, string relay)
    {
        Assert.Equal(relay, ScrolllerRelay.ToRelayPath(url));
        Assert.Equal(new System.Uri(url.Replace("http://", "https://")).AbsoluteUri.ToLowerInvariant(),
            ScrolllerRelay.FromRelayPath(relay)!.ToLowerInvariant());
    }

    [Theory]
    [InlineData("https://api.scrolller.com/api/v2/graphql")]   // the API: never handed to a page
    [InlineData("https://scrolller.com/r/hypno")]               // the site
    [InlineData("https://www.scrolller.com/a.jpg")]
    [InlineData("https://images.scrolller.com/page.html")]      // not media
    [InlineData("https://notscrolller.com/a.jpg")]
    [InlineData("https://images.scrolller.com.evil.test/a.jpg")]
    [InlineData("ccp_temp_remote_x.jpg")]
    [InlineData(null)]
    public void Anything_else_stays_as_it_is(string? url) => Assert.Null(ScrolllerRelay.ToRelayPath(url));

    [Theory]
    [InlineData("/scrolller-media/api/v2/graphql.jpg")]
    [InlineData("/scrolller-media/www/a.jpg")]
    [InlineData("/scrolller-media/images/../../etc/passwd")]
    [InlineData("/scrolller-media/images/a.html")]
    [InlineData("/scrolller-media/evil.test%2f/a.jpg")]
    [InlineData("/scrolller-media/images.evil.test/a.jpg")]
    [InlineData("/scrolller-media/@evil.test/a.jpg")]
    [InlineData("/scrolller-media/images")]
    [InlineData("/ccp.assets/a.jpg")]
    public void The_relay_never_maps_onto_anything_but_scrolller_media(string path)
    {
        var url = ScrolllerRelay.FromRelayPath(path);
        Assert.True(url == null || (new System.Uri(url).Host.EndsWith(".scrolller.com") && url.EndsWith(".jpg")), url);
        if (url != null) Assert.DoesNotContain("api.", url);
    }

    [Theory]
    [InlineData(true, false, false, true, (int)ScrolllerRelayRoute.Block)]
    [InlineData(true, false, true, true, (int)ScrolllerRelayRoute.Block)]           // off beats a cached copy
    [InlineData(true, true, true, false, (int)ScrolllerRelayRoute.ServeCached)]     // cached beats a spent share
    [InlineData(true, true, false, true, (int)ScrolllerRelayRoute.FetchThenServe)]
    [InlineData(true, true, false, false, (int)ScrolllerRelayRoute.Refuse)]
    [InlineData(false, true, true, true, (int)ScrolllerRelayRoute.NotFound)]
    public void Route_matrix(bool scrolller, bool enabled, bool cached, bool fresh, int want)
        => Assert.Equal((ScrolllerRelayRoute)want, ScrolllerRelay.Decide(scrolller ? "https://images.scrolller.com/a.jpg" : null, enabled, cached, fresh));

    [Theory]
    [InlineData(true, false, true, (int)ScrolllerRelayRoute.NotFound)]       // uncached: never a fetch
    [InlineData(true, false, false, (int)ScrolllerRelayRoute.NotFound)]
    [InlineData(true, true, true, (int)ScrolllerRelayRoute.ServeCached)]     // cached: headers only
    [InlineData(false, true, true, (int)ScrolllerRelayRoute.Block)]          // the off switch still wins
    public void A_head_never_fetches(bool enabled, bool cached, bool fresh, int want)
        => Assert.Equal((ScrolllerRelayRoute)want, ScrolllerRelay.Decide("https://images.scrolller.com/a.mp4", enabled, cached, fresh, headOnly: true));

    [Fact]
    public void A_frame_has_every_media_url_rewritten_at_any_depth_and_nothing_else()
    {
        var json = "{\"type\":\"assets-append\",\"assets\":[{\"id\":\"scrolller/hypno/1\",\"url\":\"https://images.scrolller.com/a/1.mp4\","
                   + "\"posterUrl\":\"https://images.scrolller.com/a/1.jpg\",\"link\":\"https://scrolller.com/r/hypno\",\"at\":\"2026-10-23T10:00:00Z\",\"w\":0.5}]}";
        var outJson = ScrolllerRelay.RewriteJson(json);
        Assert.Contains("\"at\":\"2026-10-23T10:00:00Z\"", outJson);   // dates are not re-parsed
        var o = JObject.Parse(outJson);
        var a = o["assets"]![0]!;
        Assert.Equal("/scrolller-media/images/a/1.mp4", (string?)a["url"]);
        Assert.Equal("/scrolller-media/images/a/1.jpg", (string?)a["posterUrl"]);
        Assert.Equal("https://scrolller.com/r/hypno", (string?)a["link"]);
        Assert.Equal("scrolller/hypno/1", (string?)a["id"]);
        Assert.Equal(0.5, (double)a["w"]!);
        Assert.Equal(ScrolllerRelay.RewriteJson(json), ScrolllerRelay.RewriteJson(ScrolllerRelay.RewriteJson(json)));
    }

    [Fact]
    public void A_frame_without_scrolller_is_returned_as_the_same_string()
    {
        const string json = "{\"type\":\"init\",\"url\":\"/ccp.assets/a.jpg\"}";
        Assert.Same(json, ScrolllerRelay.RewriteJson(json));
        Assert.Equal("not json scrolller.com", ScrolllerRelay.RewriteJson("not json scrolller.com"));
    }

    [Fact]
    public void With_the_pages_origin_the_relay_url_is_absolute_on_it()
    {
        const string json = "{\"images\":[\"https://images.scrolller.com/a/2.gif\"]}";
        var o = JObject.Parse(ScrolllerRelay.RewriteJson(json, "http://127.0.0.1:5555/"));
        Assert.Equal("http://127.0.0.1:5555/scrolller-media/images/a/2.gif", (string?)o["images"]![0]);
        Assert.Equal("http://127.0.0.1:5555", ScrolllerRelay.LoopbackOrigin(new System.Uri("http://127.0.0.1:5555/fyp/index.html?ccp_t=1")));
        Assert.Null(ScrolllerRelay.LoopbackOrigin(new System.Uri("https://app.cclabs.app/shop")));
        Assert.Null(ScrolllerRelay.LoopbackOrigin(null));
    }
}
