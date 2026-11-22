using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>T&amp;C pictures (CONTRACT.md sections 1, 3.1 and 5) and the Sparkles medal claim.</summary>
public class TcMediaRulesTests
{
    [Fact]
    public void Every_theme_id_the_page_can_send_has_subs()
    {
        foreach (var id in TcFrameGate.Niches)
            Assert.True(TcMediaRules.ThemeSubs.TryGetValue(id, out var subs) && subs.Length > 0, id);
        Assert.Equal(TcFrameGate.Niches.Count, TcMediaRules.ThemeSubs.Count);
    }

    [Fact]
    public void No_list_or_no_known_id_is_the_general_mix()
    {
        Assert.Equal(TcMediaRules.GeneralSubs, TcMediaRules.SubsFor(Array.Empty<string>()));
        Assert.Equal(TcMediaRules.GeneralSubs, TcMediaRules.SubsFor(null));
        Assert.Equal(TcMediaRules.GeneralSubs, TcMediaRules.SubsFor(new[] { "nope" }));
    }

    [Fact]
    public void One_theme_is_its_subs_and_two_are_an_even_mix()
    {
        Assert.Equal(TcMediaRules.ThemeSubs["bimbo"], TcMediaRules.SubsFor(new[] { "bimbo" }));
        var mix = TcMediaRules.SubsFor(new[] { "bimbo", "drone" });
        Assert.Equal(new[] { "bimbofication", "Dronification", "Bimbos", "ShinyPorn", "bimbo", "LatexFetish" }, mix);
        Assert.Equal(TcMediaRules.ThemeSubs["goon"], TcMediaRules.SubsFor(new[] { "goon", "goon" }));
    }

    [Theory]
    [InlineData("scrolller", true, false, false, true)]
    [InlineData("scrolller", false, false, false, false)]   // no app consent, never
    [InlineData("local", true, false, false, false)]        // the app's source is local
    [InlineData(null, true, false, false, false)]
    [InlineData("scrolller", true, true, false, false)]     // offline mode
    [InlineData("scrolller", true, false, true, false)]     // switched off in this window
    public void The_gate(string? source, bool consent, bool offline, bool offInWindow, bool expected)
        => Assert.Equal(expected, TcMediaRules.PicturesOn(source, consent, offline, offInWindow));

    [Theory]
    [InlineData("scrolller", false, false, "declined")]
    [InlineData("local", true, false, "disabled")]
    [InlineData("scrolller", true, true, "disabled")]
    [InlineData("scrolller", true, false, null)]
    public void Refusal_reasons_use_the_contract_codes(string source, bool consent, bool offline, string? expected)
        => Assert.Equal(expected, TcMediaRules.RefusalReason(source, consent, offline));

    [Fact]
    public void The_setting_echo_carries_a_reason_only_when_off()
    {
        var off = TcMediaRules.SettingEcho(false, "declined");
        Assert.Equal("setting", (string?)off["type"]);
        Assert.Equal("media.remoteConsent", (string?)off["key"]);
        Assert.False((bool)off["value"]!);
        Assert.Equal("declined", (string?)off["reason"]);
        Assert.Null(TcMediaRules.SettingEcho(true, "declined")["reason"]);
    }

    [Fact]
    public void The_cap_is_device_origin_on_ccp_assets_with_clips()
    {
        var cap = TcMediaRules.ScrolllerCap(true);
        Assert.True((bool)cap["consent"]!);
        Assert.Equal("device", (string?)cap["origin"]);
        Assert.Equal("https://ccp.assets", (string?)cap["mediaOrigin"]);
        Assert.True((bool)cap["clips"]!);
        var init = TcHostRules.InitFrame("en", MotionLevel.Full, false, null, TcRules.Caps("small", false), picturesOn: true, sparkles: true);
        Assert.True((bool)init["caps"]!["scrolller"]!["consent"]!);
        Assert.True((bool)init["caps"]!["sparkles"]!);
    }

    [Fact]
    public void The_feed_holds_at_most_36_rows_about_a_third_clips()
    {
        var stills = Enumerable.Range(0, 50).Select(i => $"https://ccp.assets/.temp/s{i}.jpg").ToList();
        var clips = Enumerable.Range(0, 50).Select(i => $"https://ccp.assets/.temp/c{i}.mp4").ToList();
        var feed = TcMediaRules.Feed(stills, clips, 0);
        Assert.Equal("scrolller-feed", (string?)feed["type"]);
        Assert.Equal(1, (int)feed["v"]!);
        var rows = (JArray)feed["rows"]!;
        Assert.Equal(TcMediaRules.MaxRows, rows.Count);
        Assert.Equal(12, rows.Count(r => (string?)r["kind"] == "video"));
        Assert.All(rows, r => Assert.StartsWith("https://ccp.assets/", (string?)r["url"]));
    }

    [Fact]
    public void The_feed_moves_with_the_offset_and_wraps()
    {
        var stills = new[] { "a", "b", "c" };
        var rows = (JArray)TcMediaRules.Feed(stills, Array.Empty<string>(), 4)["rows"]!;
        Assert.Equal(new[] { "b", "c", "a" }, rows.Select(r => (string?)r["url"]));
        Assert.Empty((JArray)TcMediaRules.Feed(Array.Empty<string>(), Array.Empty<string>(), 0)["rows"]!);
        var onlyClips = (JArray)TcMediaRules.Feed(Array.Empty<string>(), new[] { "x" }, 0)["rows"]!;
        Assert.Single(onlyClips);
    }

    [Fact]
    public void The_set_setting_gate_reads_the_press()
    {
        Assert.True(TcFrameGate.Check(JToken.Parse("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}")).Frame!.Ask);
        Assert.False(TcFrameGate.Check(JToken.Parse("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":true}")).Frame!.Ask);
    }

    [Fact]
    public void The_claim_detail_is_the_ending_and_minutes_only()
    {
        var d = TcMediaRules.ClaimDetail("llama_expo", 71);
        Assert.Equal(2, d.Count);
        Assert.Equal("llama_expo", (string?)d["ending"]);
        Assert.Equal(71, (int)d["minutes"]!);
        Assert.Equal(0, (int)TcMediaRules.ClaimDetail("x", -3)["minutes"]!);
    }

    // ---- source pins: the media partial is never run in a test ----

    private static string Src(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Tc")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Tc", file));
    }

    [Fact]
    public void Pictures_use_the_shared_pool_behind_the_gate_and_close_releases_them()
    {
        var media = Src("TcHostService.Media.cs");
        Assert.Contains("GoonOnlineMedia.ForGame(", media);
        Assert.Contains("TcMediaRules.PicturesOn(s.MediaSource, s.HasRemoteMediaConsent, s.OfflineMode", media);
        Assert.Contains("if (PicturesOnNow) StartPictures();", media);
        Assert.Contains("TcMediaRules.SubsFor(_pictureNiches)", media);
        Assert.Contains("DisposePictures();", Src("TcHostService.cs"));
    }

    [Fact]
    public void The_medal_is_always_the_same_claim_and_the_receipt_goes_back_unchanged()
    {
        var media = Src("TcHostService.Media.cs");
        Assert.Contains("SparklesGameEnd.Claim(GameId, \"solo\"", media);
        Assert.Contains("SparklesResult.ForMedal(TcRules.EndingMedal)", media);
        Assert.Contains("slipHost?.Post(new JRaw(json))", media);
        Assert.DoesNotContain("life", media);
    }
}
