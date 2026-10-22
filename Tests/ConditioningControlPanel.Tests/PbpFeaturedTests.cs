using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Tonight Board's featured chess match: the wire of GET /v2/pbp/featured, the poll
/// plan, and the card, read through the real English strings.</summary>
public class PbpFeaturedTests
{
    private const string Id = "m_0123456789abcdef";

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static Dictionary<string, string> Language(string file) =>
        JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, file)))
            .Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? "");

    private static readonly Lazy<Dictionary<string, string>> English = new(() => Language("en.json"));

    private static string Loc(string key) => English.Value.TryGetValue(key, out var v) ? v : key;

    private static string Body(string match, bool pinned = false) =>
        "{\"match\":" + match + ",\"pinned\":" + (pinned ? "true" : "false") + ",\"rotate_ms\":20000,\"server_now_ms\":1760000012345}";

    private static string Match(string id = Id, string white = "\"Ann\"", string black = "\"Bo\"", int moves = 12, int watchers = 3) =>
        "{\"id\":\"" + id + "\",\"white\":" + white + ",\"black\":" + black + ",\"moves\":" + moves +
        ",\"watchers\":" + watchers + ",\"started_ms\":1760000000000,\"initial_ms\":300000,\"increment_ms\":2000}";

    // ---- the wire -------------------------------------------------------------------------

    [Fact]
    public void Wire_reads_a_rotated_match()
    {
        var (outcome, reply) = PbpFeaturedWire.Parse(200, Body(Match()));
        Assert.Equal(PbpFeaturedOutcome.Ok, outcome);
        Assert.Equal(new PbpFeaturedMatch(Id, "Ann", "Bo", 12, 3), reply.Match);
        Assert.False(reply.Pinned);
    }

    [Fact]
    public void Wire_reads_a_pin_and_seat_objects()
    {
        var (_, reply) = PbpFeaturedWire.Parse(200, Body(Match(white: "{\"display_name\":\" Ann \"}", black: "{\"name\":\"Bo\"}"), pinned: true));
        Assert.True(reply.Pinned);
        Assert.Equal("Ann", reply.Match!.White);
        Assert.Equal("Bo", reply.Match.Black);
    }

    [Fact]
    public void Wire_null_match_is_ok_and_empty()
    {
        var (outcome, reply) = PbpFeaturedWire.Parse(200, "{\"match\":null}");
        Assert.Equal(PbpFeaturedOutcome.Ok, outcome);
        Assert.Null(reply.Match);
    }

    [Fact]
    public void Wire_not_deployed_is_dark()
    {
        Assert.Equal(PbpFeaturedOutcome.Dark, PbpFeaturedWire.Parse(404, "{\"error\":\"not_deployed\"}").Outcome);
        Assert.Equal(PbpFeaturedOutcome.Error, PbpFeaturedWire.Parse(404, "<html>").Outcome);
        Assert.Equal(PbpFeaturedOutcome.Error, PbpFeaturedWire.Parse(500, Body(Match())).Outcome);
        Assert.Equal(PbpFeaturedOutcome.Error, PbpFeaturedWire.Parse(200, "{\"players\":[]}").Outcome);
        Assert.Equal(PbpFeaturedOutcome.Error, PbpFeaturedWire.Parse(200, null).Outcome);
    }

    [Theory]
    [InlineData("u_123", "\"Ann\"", "\"Bo\"")]
    [InlineData(Id, "\"\"", "\"Bo\"")]
    [InlineData(Id, "\"Ann\"", "null")]
    [InlineData(Id, "42", "\"Bo\"")]
    public void Wire_a_bad_id_or_missing_name_is_no_match(string id, string white, string black)
    {
        var (outcome, reply) = PbpFeaturedWire.Parse(200, Body(Match(id, white, black)));
        Assert.Equal(PbpFeaturedOutcome.Ok, outcome);
        Assert.Null(reply.Match);
    }

    [Fact]
    public void Wire_caps_names_and_clamps_counts()
    {
        var longName = new string('x', 80);
        var (_, reply) = PbpFeaturedWire.Parse(200, Body(Match(white: "\"" + longName + "\"", moves: -4, watchers: -1)));
        Assert.Equal(PbpFeaturedWire.MaxName, reply.Match!.White.Length);
        Assert.Equal(0, reply.Match.Moves);
        Assert.Equal(0, reply.Match.Watchers);
    }

    // ---- the poll plan --------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Poll_runs_only_while_the_board_is_on_screen(bool visible, bool minimized, bool runs) =>
        Assert.Equal(runs, PbpFeaturedPoll.ShouldRun(visible, minimized));

    [Fact]
    public void Poll_backs_off_on_errors_and_rests_when_dark()
    {
        var t = PbpFeaturedPoll.Base;
        Assert.Equal(TimeSpan.FromSeconds(20), t);
        t = PbpFeaturedPoll.Next(t, PbpFeaturedOutcome.Error);
        Assert.Equal(TimeSpan.FromSeconds(40), t);
        for (int i = 0; i < 10; i++) t = PbpFeaturedPoll.Next(t, PbpFeaturedOutcome.Error);
        Assert.Equal(PbpFeaturedPoll.Max, t);
        Assert.Equal(PbpFeaturedPoll.Base, PbpFeaturedPoll.Next(t, PbpFeaturedOutcome.Ok));
        Assert.Equal(PbpFeaturedPoll.Dark, PbpFeaturedPoll.Next(t, PbpFeaturedOutcome.Dark));
    }

    [Fact]
    public void Poll_keeps_the_last_match_through_a_short_outage_only()
    {
        var last = PbpFeaturedWire.Parse(200, Body(Match())).Reply;
        Assert.Equal(last, PbpFeaturedPoll.After(last, PbpFeaturedOutcome.Error, PbpFeaturedReply.None, TimeSpan.FromSeconds(30)));
        Assert.Null(PbpFeaturedPoll.After(last, PbpFeaturedOutcome.Error, PbpFeaturedReply.None, TimeSpan.FromMinutes(5)).Match);
        Assert.Null(PbpFeaturedPoll.After(last, PbpFeaturedOutcome.Dark, PbpFeaturedReply.None, TimeSpan.Zero).Match);
        Assert.Null(PbpFeaturedPoll.After(last, PbpFeaturedOutcome.Ok, PbpFeaturedReply.None, TimeSpan.Zero).Match);
    }

    // ---- the card -------------------------------------------------------------------------

    [Fact]
    public void Card_no_match_no_card()
    {
        Assert.Null(FeaturedCards.Decide(null, Loc));
        Assert.Null(FeaturedCards.Decide(PbpFeaturedReply.None, Loc));
    }

    [Fact]
    public void Card_rotated_match_reads_live_now_with_watchers()
    {
        var card = FeaturedCards.Decide(PbpFeaturedWire.Parse(200, Body(Match())).Reply, Loc)!;
        Assert.Equal(FeaturedCards.CardId, card.Id);
        Assert.Equal(BillboardCardKind.Live, card.Kind);
        Assert.Equal("live now", card.Eyebrow);
        Assert.Equal("Ann vs Bo", card.Title);
        Assert.Equal("12 moves, 3 watching", card.Line);
        Assert.Equal(BillboardActionKind.Callback, card.Action.Kind);
        Assert.Equal("watch:" + Id, card.Action.Target);
        Assert.Equal("Watch", card.Action.Label);
        Assert.Contains(card.ArtKey, CardArt.All);
        Assert.True(File.Exists(Path.Combine(SourceRoots.RepoRoot, "Assets", (string)card.ArtData!)));   // art lives in /Assets
    }

    [Fact]
    public void Card_pinned_match_reads_featured_and_skips_zero_watchers()
    {
        var card = FeaturedCards.Decide(PbpFeaturedWire.Parse(200, Body(Match(watchers: 0), pinned: true)).Reply, Loc)!;
        Assert.Equal("featured match", card.Eyebrow);
        Assert.Equal("12 moves", card.Line);
    }

    [Theory]
    [InlineData("watch:" + Id, true)]
    [InlineData("watch:u_1234567890", false)]
    [InlineData("join:chess:" + Id, false)]
    [InlineData(null, false)]
    public void Card_target_reads_back_only_its_own_watch(string? target, bool ok)
    {
        Assert.Equal(ok, FeaturedCards.TryParseTarget(target, out var id));
        Assert.Equal(ok ? Id : string.Empty, id);
    }

    [Fact]
    public void Every_language_carries_the_featured_keys()
    {
        var files = Directory.GetFiles(SourceRoots.LanguagesDirectory, "*.json");
        Assert.Equal(10, files.Length);
        var keys = new[]
        {
            "billboard_card_featured_pinned", "billboard_card_featured_rotating", "billboard_card_featured_line_watching",
            "lobby_row_versus", "lobby_moves", "lobby_btn_watch",
        };
        foreach (var f in files)
        {
            var lang = Language(Path.GetFileName(f));
            foreach (var k in keys)
                Assert.True(lang.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v), Path.GetFileName(f) + " lacks " + k);
            Assert.Contains("{0}", lang["billboard_card_featured_line_watching"]);
            Assert.Contains("{1}", lang["billboard_card_featured_line_watching"]);
        }
    }
}
