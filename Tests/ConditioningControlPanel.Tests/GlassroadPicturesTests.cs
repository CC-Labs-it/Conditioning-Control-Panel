using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Glassroad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class GlassroadPicturesTests
{
    [Theory]
    // no app consent: nothing, whatever the player pressed
    [InlineData(true, "online", false, false, false)]
    [InlineData(null, "online", false, false, false)]
    // offline: nothing
    [InlineData(true, "online", true, true, false)]
    // before any press it follows the app-wide source
    [InlineData(null, "online", true, false, true)]
    [InlineData(null, "local", true, false, false)]
    [InlineData(null, null, true, false, false)]
    // the player's own press decides
    [InlineData(true, "local", true, false, true)]
    [InlineData(false, "online", true, false, false)]
    public void The_gate_needs_consent_and_follows_the_press(bool? own, string? source, bool consent, bool offline, bool expected)
        => Assert.Equal(expected, GlassroadRules.PicturesOn(own, source, consent, offline));

    [Fact]
    public void The_feed_is_a_wrapping_window_with_no_repeats()
    {
        var urls = Enumerable.Range(0, 20).Select(i => $"https://ccp.assets/.temp/{i}.jpg").ToList();
        var rows = (JArray)GlassroadRules.PicturesFeed(urls, 16, new[] { ("hypno", "Hypno") })["rows"]!;
        Assert.Equal(GlassroadRules.PicturesRows, rows.Count);
        Assert.Equal("https://ccp.assets/.temp/16.jpg", (string?)rows[0]["url"]);
        Assert.Equal("https://ccp.assets/.temp/7.jpg", (string?)rows[11]["url"]);
        Assert.Equal(rows.Count, rows.Select(r => (string?)r["url"]).Distinct().Count());
        Assert.All(rows, r => Assert.Equal("image", (string?)r["kind"]));
    }

    [Fact]
    public void A_short_or_empty_pool_is_only_what_it_holds()
    {
        var two = (JArray)GlassroadRules.PicturesFeed(new[] { "a", "b" }, 7, new (string, string)[0])["rows"]!;
        Assert.Equal(new[] { "b", "a" }, two.Select(r => (string?)r["url"]));
        var off = GlassroadRules.PicturesFeed(new string[0], 3, new[] { ("hypno", "Hypno") });
        Assert.Empty((JArray)off["rows"]!);
        Assert.Equal("hypno", (string?)off["catalog"]![0]!["id"]);
    }

    [Fact]
    public void Every_exit_hands_the_files_back()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services"))) dir = dir.Parent;
        Assert.True(dir != null);
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Glassroad", "GlassroadHostService.cs"));
        Assert.Contains("DisposePictures();", host);
    }
}
