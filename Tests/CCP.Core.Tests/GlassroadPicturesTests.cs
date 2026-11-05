using System;
using System.Linq;
using ConditioningControlPanel.Services.Glassroad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class GlassroadPicturesTests
{
    [Theory]
    [InlineData(true, "online", false, false, false)]
    [InlineData(null, "online", false, false, false)]
    [InlineData(true, "online", true, true, false)]
    [InlineData(null, "online", true, false, true)]
    [InlineData(null, "LOCAL", true, false, false)]
    [InlineData(null, null, true, false, false)]
    [InlineData(true, "local", true, false, true)]
    [InlineData(false, "online", true, false, false)]
    public void Gate(bool? own, string? source, bool consent, bool offline, bool expected)
        => Assert.Equal(expected, GlassroadPictures.On(own, source, consent, offline));

    [Fact]
    public void FeedWrapsBoundsAndLabelsStills()
    {
        var urls = Enumerable.Range(0, 20).Select(i => $"https://example.test/{i}.jpg").ToArray();
        var feed = GlassroadPictures.Feed(urls, 16, new[] { ("hypno", "Hypno") });
        var rows = (JArray)feed["rows"]!;
        Assert.Equal(12, rows.Count);
        Assert.Equal(urls[16], (string?)rows[0]["url"]);
        Assert.Equal(urls[7], (string?)rows[11]["url"]);
        Assert.Equal(12, rows.Select(r => (string?)r["url"]).Distinct().Count());
        Assert.All(rows, r => Assert.Equal("image", (string?)r["kind"]));
        Assert.Equal("hypno", (string?)feed["catalog"]![0]!["id"]);
        Assert.Equal(new[] { "b", "a" }, ((JArray)GlassroadPictures.Feed(new[] { "a", "b" }, -1, Array.Empty<(string, string)>())["rows"]!).Select(r => (string?)r["url"]));
        Assert.Empty((JArray)GlassroadPictures.Feed(Array.Empty<string>(), 3, Array.Empty<(string, string)>())["rows"]!);
    }
}
