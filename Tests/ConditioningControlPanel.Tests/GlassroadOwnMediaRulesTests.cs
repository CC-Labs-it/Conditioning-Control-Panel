using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Glassroad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Racing Thoughts 3D "My files" on desktop shows the app's own ticked library (glassroad CONTRACT.md 7, "Desktop library").</summary>
public class GlassroadOwnMediaRulesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ccp-race-own", "assets");

    private static string In(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    private static GlassroadOwnMediaRules.Source Img(params string[] parts) => new(In(parts), true);

    private static JArray Rows(IEnumerable<GlassroadOwnMediaRules.Source> sources)
        => GlassroadOwnMediaRules.Rows(sources, Root, new Random(7));

    [Fact]
    public void The_init_caps_name_the_library_and_its_origin()
    {
        var init = GlassroadRules.InitFrame(null, "Kit", 0, false, 1, 1);
        var own = (JObject)init["caps"]!["ownMedia"]!;
        Assert.True((bool)own["library"]!);
        Assert.Equal("https://ccp.assets", (string?)own["mediaOrigin"]);
        // the Scrolller block is unchanged by it
        Assert.False((bool)init["caps"]!["scrolller"]!["consent"]!);
    }

    [Fact]
    public void Rows_are_ccp_assets_urls_with_the_page_kind()
    {
        var rows = Rows(new[] { Img("images", "a b.jpg"), new GlassroadOwnMediaRules.Source(In("videos", "c.mp4"), false) });
        Assert.Equal(2, rows.Count);
        Assert.Equal("https://ccp.assets/images/a%20b.jpg", (string?)rows[0]["url"]);
        Assert.Equal("image", (string?)rows[0]["kind"]);
        Assert.Equal("https://ccp.assets/videos/c.mp4", (string?)rows[1]["url"]);
        Assert.Equal("video", (string?)rows[1]["kind"]);
        var frame = GlassroadOwnMediaRules.Frame(rows);
        Assert.Equal("own-media", (string?)frame["type"]);
        Assert.Equal(1, (int)frame["v"]!);
    }

    [Fact]
    public void Identity_is_the_whole_path_so_two_folders_with_one_name_stay_two()
    {
        var rows = Rows(new[] { Img("images", "one", "x.png"), Img("images", "two", "x.png"), Img("images", "one", "x.png") });
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => (string?)r["url"]).Distinct().Count());
    }

    [Fact]
    public void Files_outside_the_mapping_remote_entries_and_unshowable_kinds_are_dropped()
    {
        var outside = Path.Combine(Path.GetTempPath(), "ccp-race-own", "elsewhere", "y.jpg");
        var rows = Rows(new[]
        {
            new GlassroadOwnMediaRules.Source(outside, true),
            new GlassroadOwnMediaRules.Source("https://cdn.example/z.jpg", true),
            Img("images", "pic.bmp"),
            Img("images", "clip.mp4"),                      // a clip named as a picture
            new GlassroadOwnMediaRules.Source(In("videos", "v.avi"), false),
            new GlassroadOwnMediaRules.Source("", true),
            Img("images", "ok.webp"),
        });
        Assert.Single(rows);
        Assert.Equal("https://ccp.assets/images/ok.webp", (string?)rows[0]["url"]);
        Assert.Empty(GlassroadOwnMediaRules.Rows(new[] { Img("a.jpg") }, null, new Random(1)));
        Assert.Empty(GlassroadOwnMediaRules.Rows(null, Root, new Random(1)));
    }

    [Fact]
    public void A_big_library_is_capped_with_a_random_spread_not_the_first_files()
    {
        var many = Enumerable.Range(0, 400).Select(i => Img("images", $"p{i:D3}.jpg")).ToList();
        var rows = Rows(many);
        Assert.Equal(GlassroadOwnMediaRules.MaxRows, rows.Count);
        var firsts = many.Take(GlassroadOwnMediaRules.MaxRows).Select(s => "https://ccp.assets/images/" + Path.GetFileName(s.Path));
        Assert.NotEqual(firsts, rows.Select(r => (string)r["url"]!));
        Assert.Equal(rows.Count, rows.Select(r => (string?)r["url"]).Distinct().Count());
    }

    [Fact]
    public void The_host_reads_the_active_walk_and_answers_the_page_ask()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "ConditioningControlPanel", "Services", "Glassroad")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var svc = Path.Combine(dir!, "ConditioningControlPanel", "Services", "Glassroad");
        var own = File.ReadAllText(Path.Combine(svc, "GlassroadHostService.OwnMedia.cs"));
        Assert.Contains("DtrhAssetManifest.EnumerateActive()", own);
        Assert.DoesNotContain("HasRemoteMediaConsent", own);   // local files never ask the remote consent
        Assert.Contains("case \"own-media-list\":", File.ReadAllText(Path.Combine(svc, "GlassroadHostService.cs")));
    }
}
