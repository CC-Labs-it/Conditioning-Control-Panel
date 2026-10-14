using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Chess spectate v2, desktop half: the stands' effects prefs through the page frames,
/// the mediaAvailable probe rule, and the public watch routes the host relays without a token.</summary>
public class PbpWatchV2Tests
{
    private const string Mid = "m_0123456789abcdef";

    private static JObject Frame(string key, JToken value) =>
        new() { ["type"] = "pbp:setting", ["key"] = key, ["value"] = value };

    [Fact]
    public void Watch_effects_default_on_and_amount_normal()
    {
        var s = new AppSettings();
        Assert.True(s.PbpWatchEffects);
        Assert.Equal("normal", s.PbpWatchAmount);
        Assert.True(PbpWatchRules.WatchEffects(null));
        Assert.Equal("normal", PbpWatchRules.WatchAmount(null));
    }

    [Fact]
    public void Watch_effects_round_trip_through_the_page_frame_and_json()
    {
        var s = new AppSettings();
        Assert.True(PbpWatchRules.TryRead(Frame("watchEffects", false), out var key, out var value));
        Assert.Equal("watchEffects", key);
        Assert.True(PbpWatchRules.Apply(s, key, value));
        Assert.False(s.PbpWatchEffects);
        Assert.False(PbpWatchRules.Apply(s, key, value));   // same value: nothing to save

        Assert.True(PbpWatchRules.TryRead(Frame("watchAmount", "more"), out key, out value));
        Assert.True(PbpWatchRules.Apply(s, key, value));
        Assert.Equal("more", s.PbpWatchAmount);

        var json = JsonConvert.SerializeObject(s);
        Assert.Contains("\"pbpWatchEffects\":false", json);
        Assert.Contains("\"pbpWatchAmount\":\"more\"", json);
        var back = JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.False(back.PbpWatchEffects);
        Assert.Equal("more", back.PbpWatchAmount);
        // The other host-kept toggle is untouched.
        Assert.True(back.PbpLetPeopleWatch);
    }

    [Theory]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"watchEffects\",\"value\":\"false\"}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"watchEffects\",\"value\":0}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"watchAmount\",\"value\":\"max\"}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"watchAmount\",\"value\":\"Normal\"}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"watchAmount\",\"value\":2}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"watchAmount\",\"value\":null}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"watchAmount\"}")]
    [InlineData("{\"type\":\"pbp:setting\",\"key\":\"mediaAvailable\",\"value\":true}")]
    public void Watch_setting_frames_refuse_wrong_types_and_unknown_values(string json)
    {
        Assert.False(PbpWatchRules.TryRead(JObject.Parse(json), out _, out _));
    }

    [Fact]
    public void Apply_refuses_a_value_of_the_wrong_type_for_its_key()
    {
        var s = new AppSettings();
        Assert.False(PbpWatchRules.Apply(s, "watchAmount", true));
        Assert.False(PbpWatchRules.Apply(s, "watchAmount", "loud"));
        Assert.False(PbpWatchRules.Apply(s, "watchEffects", "false"));
        Assert.False(PbpWatchRules.Apply(s, "letPeopleWatch", "false"));
        Assert.False(PbpWatchRules.Apply(s, "watchEffects", null));
        Assert.True(s.PbpWatchEffects);
        Assert.Equal("normal", s.PbpWatchAmount);
    }

    [Fact]
    public void A_hand_edited_amount_reads_as_normal()
    {
        var back = JsonConvert.DeserializeObject<AppSettings>("{\"pbpWatchAmount\":\"huge\"}")!;
        Assert.Equal("normal", back.PbpWatchAmount);
        var s = new AppSettings { PbpWatchAmount = "less" };
        Assert.Equal("less", s.PbpWatchAmount);
        s.PbpWatchAmount = null!;
        Assert.Equal("normal", s.PbpWatchAmount);
    }

    [Fact]
    public void Amounts_match_the_page_dial()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "repo root not found from " + AppContext.BaseDirectory);
        var prefs = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Resources", "web",
            "piecebypiece", "game", "preferences.js"));
        var m = Regex.Match(prefs, @"const AMOUNTS = \[([^\]]*)\]");
        Assert.True(m.Success, "AMOUNTS not found in game/preferences.js");
        var page = Regex.Matches(m.Groups[1].Value, "'([^']*)'").Select(x => x.Groups[1].Value).ToArray();
        Assert.Equal(page, PbpWatchRules.Amounts);
        Assert.Contains(PbpWatchRules.DefaultAmount, page);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, 3, true)]
    public void Media_available_is_local_or_online(bool local, int channels, bool expected)
    {
        var list = Enumerable.Range(0, channels).Select(i => "sub" + i).ToList();
        Assert.Equal(expected, PbpMediaRules.MediaAvailable(local, list));
    }

    [Fact]
    public void Media_available_follows_the_consent_rule_for_the_online_half()
    {
        Assert.False(PbpMediaRules.MediaAvailable(false, null));
        // A local source with no consent and no pick: no niches may be fetched, so no online half.
        var none = PbpMediaRules.ChannelsFor(true, false, "local", false, "", new[] { "a" }, new[] { "b" });
        Assert.False(PbpMediaRules.MediaAvailable(false, none));
        // Online picked but no consent: still nothing.
        var noConsent = PbpMediaRules.ChannelsFor(true, false, "online", false, "", null, new[] { "b" });
        Assert.False(PbpMediaRules.MediaAvailable(false, noConsent));
        // A flavour picked in the game (its own opt-in) with niches: media available.
        var picked = PbpMediaRules.ChannelsFor(true, true, "local", false, "pink", new[] { "pinkniche" }, null);
        Assert.True(PbpMediaRules.MediaAvailable(false, picked));
        // "Own pictures only": the online half is shut whatever is consented.
        var own = PbpMediaRules.ChannelsFor(false, true, "online", true, "pink", new[] { "pinkniche" }, new[] { "b" });
        Assert.False(PbpMediaRules.MediaAvailable(false, own));
        Assert.True(PbpMediaRules.MediaAvailable(true, own));
    }

    [Theory]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/0", true)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/123", true)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/upto/7", true)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/reactions/586612345", true)]
    // The join heartbeat, reacting and every seat route keep the token.
    [InlineData("GET", "/v2/pbp/watch/" + Mid, false)]
    [InlineData("POST", "/v2/pbp/watch/" + Mid + "/react", false)]
    [InlineData("POST", "/v2/pbp/watch/" + Mid + "/chunk/1", false)]
    [InlineData("GET", "/v2/pbp/match/" + Mid + "/events", false)]
    [InlineData("POST", "/v2/pbp/match/" + Mid + "/fx", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/", false)]
    // The server's router is case-insensitive, takes one trailing slash and ignores the query:
    // every spelling it routes to a public handler goes without the token.
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/1?x=1", true)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/1/", true)]
    [InlineData("GET", "/V2/PBP/WATCH/M_0123456789ABCDEF/CHUNK/1", true)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/Reactions/9#x", true)]
    [InlineData("get", "/v2/pbp/watch/" + Mid + "/chunk/1", true)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/1//", false)]
    // Anything not the public shape keeps it.
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/-1", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/1/2", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/events", false)]
    [InlineData("GET", "/v2/pbp/watch/m_zz/chunk/1", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/1234567890123", false)]
    [InlineData("GET", null, false)]
    public void Public_watch_routes_are_relayed_without_the_token(string method, string? path, bool isPublic)
        => Assert.Equal(isPublic, PbpWatchRules.IsPublicWatchPath(method, path));

    [Theory]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/chunk/4", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/upto/4", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid + "/reactions/586612345", false)]
    [InlineData("GET", "/v2/pbp/watch/" + Mid, true)]
    [InlineData("POST", "/v2/pbp/watch/" + Mid + "/react", true)]
    [InlineData("POST", "/v2/pbp/match/" + Mid + "/fx", true)]
    [InlineData("GET", "/v2/pbp/match/" + Mid + "/events", true)]
    public void The_relay_request_carries_the_token_except_on_public_routes(string method, string path, bool hasToken)
    {
        using var req = PbpNetRelay.Build(Judged(path), method, path, "{\"a\":1}", "tok-123", "9.9.9");
        Assert.Equal("https://proxy.example" + path, req.RequestUri!.ToString());
        Assert.Equal(hasToken, req.Headers.Contains(PbpNetRelay.TokenHeader));
        if (hasToken) Assert.Equal("tok-123", Assert.Single(req.Headers.GetValues(PbpNetRelay.TokenHeader)));
        Assert.Equal("9.9.9", Assert.Single(req.Headers.GetValues(PbpNetRelay.VersionHeader)));
        Assert.Equal(method == "POST", req.Content != null);
        if (method == "POST") Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
    }

    /// <summary>The Uri the host's path rule hands the relay (the same call OnNetRequest makes).</summary>
    private static Uri Judged(string path)
    {
        Assert.True(ConditioningControlPanel.Services.Safety.HostNetPathRule.TryBuild("https://proxy.example", path, "/v2/pbp/", out var uri));
        return uri!;
    }

    [Fact]
    public void The_relay_sends_no_token_header_when_signed_out()
    {
        using var req = PbpNetRelay.Build(Judged("/v2/pbp/match/" + Mid), "GET", "/v2/pbp/match/" + Mid, "", "", "1.0.0");
        Assert.False(req.Headers.Contains(PbpNetRelay.TokenHeader));
    }

    [Fact]
    public void Settings_frame_carries_exactly_the_fields_the_page_reads()
    {
        var s = new AppSettings { PbpWatchEffects = false, PbpWatchAmount = "less", PbpLetPeopleWatch = false };
        var f = PbpWatchRules.SettingsFrame(15, true, new[] { "a.mp3" }, s, false, Mid);
        Assert.Equal(new[]
            {
                "type", "videoHoldSec", "reducedMotion", "whispers", "letPeopleWatch",
                "watchEffects", "watchAmount", "mediaAvailable", "spectateMatchId", "gentleIntroSpeed",
            }.OrderBy(x => x, StringComparer.Ordinal),
            f.Properties().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal("pbp:settings", (string?)f["type"]);
        Assert.Equal(JTokenType.Boolean, f["watchEffects"]!.Type);
        Assert.False((bool)f["watchEffects"]!);
        Assert.Equal("less", (string?)f["watchAmount"]);
        Assert.False((bool)f["mediaAvailable"]!);
        Assert.False((bool)f["letPeopleWatch"]!);
        Assert.Equal(Mid, (string?)f["spectateMatchId"]);
        Assert.Equal("a.mp3", (string?)f["whispers"]![0]);

        // Defaults, no spectate: the id is null (the page skips a falsy one), whispers an empty list.
        var d = PbpWatchRules.SettingsFrame(15, false, null, null, true, null);
        Assert.True((bool)d["watchEffects"]!);
        Assert.Equal("normal", (string?)d["watchAmount"]);
        Assert.True((bool)d["letPeopleWatch"]!);
        Assert.Equal(JTokenType.Null, d["spectateMatchId"]!.Type);
        Assert.Empty((JArray)d["whispers"]!);
    }

    [Theory]
    [InlineData(false, 2, null, true)]
    [InlineData(false, 2, "loading", true)]
    [InlineData(false, 2, "ready", true)]
    [InlineData(false, 2, "error", true)]
    [InlineData(false, 2, "empty", false)]
    [InlineData(false, 2, "off", false)]
    [InlineData(true, 2, "empty", true)]
    [InlineData(true, 0, "off", true)]
    public void Niches_whose_fetch_came_back_dry_are_no_source(bool local, int channels, string? state, bool expected)
    {
        var list = Enumerable.Range(0, channels).Select(i => "sub" + i).ToList();
        Assert.Equal(expected, PbpMediaRules.MediaAvailable(local, list, state));
    }

    [Fact]
    public void Folder_check_is_the_cheap_first_step()
    {
        Assert.False(PbpMediaRules.FolderHasEntries(null));
        Assert.False(PbpMediaRules.FolderHasEntries(""));
        var dir = Path.Combine(Path.GetTempPath(), "pbp-watch-v2-" + Guid.NewGuid().ToString("N"));
        Assert.False(PbpMediaRules.FolderHasEntries(dir));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(PbpMediaRules.FolderHasEntries(dir));
            File.WriteAllText(Path.Combine(dir, "x.png"), "x");
            Assert.True(PbpMediaRules.FolderHasEntries(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
