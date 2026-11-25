using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Glassroad;
using Xunit;

namespace CCP.Core.Tests;

public class GlassroadOwnMediaSelectionTests
{
    [Fact]
    public void SelectsLocalShowableFilesByWholePathAndCapsTheResult()
    {
        string root = Path.Combine(Path.GetTempPath(), "race-own-media", "assets");
        GlassroadOwnMediaSelection.Source Img(string rel) => new(Path.Combine(root, rel), true);
        string Url(string rel) => "asset/" + rel.Replace('\\', '/');
        var sources = new[] {
            Img("images/one/a.jpg"), Img("images/two/a.jpg"), Img("images/one/a.jpg"),
            Img("images/no.bmp"), new GlassroadOwnMediaSelection.Source("https://example.test/a.jpg", true),
            new GlassroadOwnMediaSelection.Source(Path.Combine(root, "..", "outside.jpg"), true),
            new GlassroadOwnMediaSelection.Source(Path.Combine(root, "videos/c.mp4"), false)
        };
        var rows = GlassroadOwnMediaSelection.Rows(sources, root, new Random(7), Url);
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Select(r => (string?)r["url"]).Distinct().Count());
        Assert.Contains(rows, r => (string?)r["url"] == "asset/videos/c.mp4" && (string?)r["kind"] == "video");
        Assert.Contains(rows, r => (string?)r["url"] == "asset/images/one/a.jpg" && (string?)r["kind"] == "image");
        var many = Enumerable.Range(0, 400).Select(i => Img($"images/{i:D3}.jpg"));
        var sampled = GlassroadOwnMediaSelection.Rows(many, root, new Random(7), Url);
        Assert.Equal(96, sampled.Count);
        Assert.Contains(sampled, r => string.CompareOrdinal((string?)r["url"], "asset/images/095.jpg") > 0);
        var frame = GlassroadOwnMediaSelection.Frame(rows);
        Assert.Equal("own-media", (string?)frame["type"]);
        Assert.Equal(1, (int?)frame["v"]);
    }
}
