using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>"Use local files" on desktop draws from the app's own ticked library (fuzz/CONTRACT.md 3.3, the library).</summary>
public class TcOwnMediaRulesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ccp-tc-own", "assets");

    private static string In(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    private static TcOwnMediaRules.Stat? Present(string _) => new TcOwnMediaRules.Stat(1234, 1_700_000_000_000);

    private static JArray Rows(IEnumerable<TcOwnMediaRules.Source> sources, Func<string, bool>? animates = null,
        Func<string, TcOwnMediaRules.Stat?>? stat = null)
        => TcOwnMediaRules.Rows(sources, Root, animates ?? (_ => false), stat ?? Present, new Random(7));

    [Fact]
    public void The_init_caps_name_the_library_and_its_origin_and_no_folder()
    {
        var init = TcHostRules.InitFrame("en", MotionLevel.Full, false, null, TcRules.Caps(TcRules.EditionSmall, false));
        var own = (JObject)init["caps"]!["ownMedia"]!;
        Assert.True((bool)own["library"]!);
        Assert.Equal("https://ccp.assets", (string?)own["mediaOrigin"]);
        Assert.Null(own["folder"]);   // the folder row and its picker stay the browser's own
    }

    [Fact]
    public void The_page_may_ask_for_the_list()
        => Assert.True(TcFrameGate.Check(JToken.Parse("{\"type\":\"own-media-list\"}")).Accepted);

    [Fact]
    public void Every_ticked_file_becomes_a_ccp_assets_row_with_its_kind()
    {
        var rows = Rows(new[]
        {
            new TcOwnMediaRules.Source(In("images", "a.jpg"), false),
            new TcOwnMediaRules.Source(In("images", "loop.gif"), false),
            new TcOwnMediaRules.Source(In("images", "moving.webp"), false),
            new TcOwnMediaRules.Source(In("images", "still.webp"), false),
            new TcOwnMediaRules.Source(In("videos", "clip one.mp4"), true),
        }, animates: p => p.EndsWith("moving.webp", StringComparison.Ordinal));

        var byName = rows.Cast<JObject>().ToDictionary(r => (string)r["name"]!);
        Assert.Equal(5, byName.Count);
        Assert.Equal("picture", (string?)byName["images/a.jpg"]["kind"]);
        Assert.Equal("image/jpeg", (string?)byName["images/a.jpg"]["type"]);
        Assert.Equal("gif", (string?)byName["images/loop.gif"]["kind"]);
        Assert.Equal("gif", (string?)byName["images/moving.webp"]["kind"]);
        Assert.Equal("picture", (string?)byName["images/still.webp"]["kind"]);
        Assert.Equal("clip", (string?)byName["videos/clip one.mp4"]["kind"]);
        Assert.Equal("https://ccp.assets/videos/clip%20one.mp4", (string?)byName["videos/clip one.mp4"]["url"]);
        Assert.Equal(1234L, (long)byName["images/a.jpg"]["size"]!);
        Assert.Equal(1_700_000_000_000L, (long)byName["images/a.jpg"]["mtime"]!);
        Assert.All(rows, r => Assert.StartsWith(TcOwnMediaRules.MediaOrigin + "/", (string)r["url"]!));
    }

    [Fact]
    public void Identity_is_the_whole_path_so_two_folders_keep_two_files_and_a_repeat_is_one()
    {
        var rows = Rows(new[]
        {
            new TcOwnMediaRules.Source(In("images", "x", "same.png"), false),
            new TcOwnMediaRules.Source(In("images", "y", "same.png"), false),
            new TcOwnMediaRules.Source(In("images", "x", "SAME.png"), false),   // the same file, another spelling
        });
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "images/x/same.png", "images/y/same.png" },
            rows.Select(r => ((string)r["name"]!).ToLowerInvariant()).OrderBy(n => n));
    }

    [Fact]
    public void Remote_entries_files_outside_the_assets_root_unshowable_and_missing_files_stay_out()
    {
        var outside = Path.Combine(Path.GetTempPath(), "ccp-tc-own", "elsewhere", "b.jpg");
        var rows = Rows(new[]
        {
            new TcOwnMediaRules.Source("https://cdn.example/a.jpg", false),
            new TcOwnMediaRules.Source(outside, false),
            new TcOwnMediaRules.Source(In("images", "scan.tiff"), false),
            new TcOwnMediaRules.Source(In("images", "icon.ico"), false),
            new TcOwnMediaRules.Source(In("videos", "old.avi"), true),
            new TcOwnMediaRules.Source(In("images", "gone.jpg"), false),
            new TcOwnMediaRules.Source(In("images", "empty.jpg"), false),
            new TcOwnMediaRules.Source(In("images", "kept.jpg"), false),
            new TcOwnMediaRules.Source("", false),
        }, stat: p => p.EndsWith("gone.jpg", StringComparison.Ordinal) ? null
            : p.EndsWith("empty.jpg", StringComparison.Ordinal) ? new TcOwnMediaRules.Stat(0, 0) : Present(p));
        Assert.Equal(new[] { "images/kept.jpg" }, rows.Select(r => (string)r["name"]!));
    }

    [Fact]
    public void Nothing_ticked_is_an_empty_answer_that_never_switches_the_source()
    {
        var frame = TcOwnMediaRules.Frame(Rows(Array.Empty<TcOwnMediaRules.Source>()));
        Assert.Equal("own-media", (string?)frame["type"]);
        Assert.Equal(1, (int)frame["v"]!);
        Assert.Empty((JArray)frame["rows"]!);
        Assert.False((bool)frame["asked"]!);
        Assert.Equal("", (string?)frame["path"]);
        Assert.Empty(TcOwnMediaRules.Rows(null, Root, _ => false, Present, new Random(1)));
        Assert.Empty(TcOwnMediaRules.Rows(new[] { new TcOwnMediaRules.Source(In("images", "a.jpg"), false) }, "", _ => false, Present, new Random(1)));
    }

    [Fact]
    public void A_big_library_is_cut_to_the_page_cap_and_only_kept_rows_are_read()
    {
        int reads = 0;
        var sources = Enumerable.Range(0, TcOwnMediaRules.MaxRows + 500)
            .Select(i => new TcOwnMediaRules.Source(In("images", $"p{i}.webp"), false)).ToList();
        int probes = 0;
        var rows = Rows(sources, animates: _ => { probes++; return false; }, stat: p => { reads++; return Present(p); });
        Assert.Equal(TcOwnMediaRules.MaxRows, rows.Count);
        Assert.Equal(TcOwnMediaRules.MaxRows, reads);
        Assert.Equal(TcOwnMediaRules.MaxRows, probes);
        Assert.Equal(rows.Count, rows.Select(r => (string)r["url"]!).Distinct().Count());
    }
}
