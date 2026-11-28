using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The chess wardrobe (7.2 shop, lane B3-C1): grants, clamps, frames and the /v2/equip sync rule.</summary>
public class PbpLookRulesTests
{
    [Theory]
    [InlineData("back", true)]
    [InlineData("wear", true)]
    [InlineData("panic", false)]
    [InlineData("app-exit", false)]
    [InlineData("failed", false)]
    public void CounterHandsFocusBack(string reason, bool expected)
        => Assert.Equal(expected, PbpLookRules.CounterHandsFocusBack(reason));

    private static readonly string[] All =
    {
        "cos.chess.set.royal-capes", "cos.chess.set.velvet-court", "cos.chess.banner.house-pink", "cos.chess.banner.crest",
        "cos.prime.banner.arena", "cos.chess.insignia.heart", "cos.chess.insignia.star",
    };

    [Fact]
    public void Owned_frame_lists_only_chess_grants_sorted()
    {
        var f = PbpLookRules.OwnedFrame(new[] { "cos.kart.body.x", "cos.chess.insignia.star", "cos.prime.banner.arena", "cos.rain.hat" });
        Assert.Equal("owned", (string?)f["type"]);
        Assert.Equal(new[] { "cos.chess.insignia.star", "cos.prime.banner.arena" }, f["grants"]!.Values<string>().ToArray());
        Assert.Empty(PbpLookRules.ChessGrants(null));
    }

    [Fact]
    public void Stock_frame_carries_the_chess_tabs_back_dates_only()
    {
        // A /v2/shop/state body as CCP-Server shop.js answers it with `later` (ccp-bugs #1738).
        var state = JObject.Parse(@"{ ""ok"": true, ""tabs"": [
            { ""id"": ""kart"", ""shelves"": [], ""later"": [ { ""id"": ""kart.body.x"", ""from"": ""2026-11-30"" } ] },
            { ""id"": ""chess"", ""shelves"": [], ""later"": [
                { ""id"": ""chess.banner.crest"", ""from"": ""2026-11-30"" },
                { ""id"": ""chess.insignia.heart"", ""from"": ""next week"" },
                { ""id"": ""rain.hat"", ""from"": ""2026-11-30"" },
                { ""id"": 7, ""from"": ""2026-11-30"" } ] } ] }");
        var f = PbpLookRules.StockFrame(state);
        Assert.Equal("pbp:stock", (string?)f["type"]);
        var later = (JArray)f["later"]!;
        Assert.Single(later);
        Assert.Equal("chess.banner.crest", (string?)later[0]["id"]);
        Assert.Equal("2026-11-30", (string?)later[0]["from"]);
        // An older server (no `later`), a refusal or junk: an empty list, never a throw.
        Assert.Empty((JArray)PbpLookRules.StockFrame(JObject.Parse(@"{ ""ok"": true, ""tabs"": [ { ""id"": ""chess"", ""shelves"": [] } ] }"))["later"]!);
        Assert.Empty((JArray)PbpLookRules.StockFrame(JObject.Parse(@"{ ""ok"": false, ""reason"": ""no_account"" }"))["later"]!);
        Assert.Empty((JArray)PbpLookRules.StockFrame(new JArray())["later"]!);
        Assert.Empty((JArray)PbpLookRules.StockFrame(null)["later"]!);
    }

    [Fact]
    public void Normal_keeps_known_ids_in_their_own_slot()
    {
        var l = PbpLookRules.Normal(JObject.Parse("{\"set\":\"chess.set.royal-capes\",\"banner\":\"chess.set.velvet-court\",\"insignia\":7}"));
        Assert.Equal(new ChessLook("chess.set.royal-capes", null, null), l);
        Assert.Equal(ChessLook.Empty, PbpLookRules.Normal(null));
        Assert.Equal(ChessLook.Empty, PbpLookRules.Normal(new JArray()));
    }

    [Fact]
    public void Clamp_takes_off_what_is_not_owned_and_keeps_a_held_prime_item()
    {
        var look = new ChessLook("chess.set.velvet-court", "prime.banner.arena", "chess.insignia.heart");
        Assert.Equal(new ChessLook(null, "prime.banner.arena", null), PbpLookRules.Clamp(look, new[] { "cos.prime.banner.arena" }));
        Assert.Equal(ChessLook.Empty, PbpLookRules.Clamp(look, null));
        Assert.Equal(look, PbpLookRules.Clamp(look, All));
    }

    [Fact]
    public void Stored_copy_round_trips_and_survives_junk()
    {
        var look = new ChessLook(null, "chess.banner.crest", "chess.insignia.star");
        var (back, at) = PbpLookRules.Read(PbpLookRules.Write(look, 1234));
        Assert.Equal(look, back);
        Assert.Equal(1234, at);
        Assert.Equal((ChessLook.Empty, 0L), PbpLookRules.Read(""));
        Assert.Equal((ChessLook.Empty, 0L), PbpLookRules.Read("{not json"));
    }

    [Fact]
    public void Each_granted_finish_round_trips_alongside_clothes_banner_and_insignia()
    {
        var dressed = new ChessLook("chess.set.velvet-court", "chess.banner.crest", "chess.insignia.star");
        foreach (var id in PbpLookRules.Catalog["finish"])
        {
            Assert.Equal("finish", PbpLookRules.SlotOf(id));
            var pick = PbpLookRules.With(dressed, id);
            var grants = All.Append(PbpLookRules.GrantOf(id));
            Assert.Equal(dressed with { Finish = id }, pick);
            Assert.Contains(PbpLookRules.GrantOf(id), PbpLookRules.ChessGrants(grants).Values<string>());
            Assert.Equal(dressed, PbpLookRules.Clamp(pick, All));
            Assert.Equal(pick, PbpLookRules.Clamp(pick, grants));
            Assert.Equal(pick, PbpLookRules.Read(PbpLookRules.Write(pick, 50)).Look);
            var value = PbpLookRules.EquipBody("u1", pick)["value"];
            Assert.Equal(id, (string?)value!["finish"]);
            var reply = new JObject { ["equip"] = new JObject { ["chess"] = value, ["updatedAt"] = 55 } };
            Assert.Equal(pick, PbpLookRules.FromEquip(reply).Look);
        }
    }

    [Fact]
    public void Legacy_page_pick_preserves_an_owned_finish_and_explicit_none_clears_it()
    {
        var saved = new ChessLook(null, null, null, "chess.finish.marble");
        var grants = new[] { "cos.chess.finish.marble", "cos.chess.insignia.star" };
        var page = new JObject { ["insignia"] = "chess.insignia.star" };
        Assert.Equal(saved with { Insignia = "chess.insignia.star" }, PbpLookRules.PagePick(page, saved, grants));
        Assert.Null(PbpLookRules.PagePick(page, saved, All).Finish);
        page["finish"] = JValue.CreateNull();
        var cleared = PbpLookRules.PagePick(page, saved, grants);
        Assert.Equal(new ChessLook(null, null, "chess.insignia.star"), cleared);
        Assert.Equal(JTokenType.Null, PbpLookRules.EquipBody("u1", cleared)["value"]!["finish"]!.Type);
        page["finish"] = "chess.finish.unknown";
        Assert.Null(PbpLookRules.PagePick(page, saved, grants).Finish);
    }

    [Fact]
    public void Board_and_cheerleader_are_owned_independently_and_survive_legacy_picks()
    {
        var saved = new ChessLook(null, null, null, null, "chess.board.theatre", "chess.companion.emi");
        var grants = new[] { "cos.chess.board.theatre", "cos.chess.companion.emi" };
        Assert.Equal(saved, PbpLookRules.PagePick(new JObject(), saved, grants));
        Assert.Equal(ChessLook.Empty, PbpLookRules.Clamp(saved, Array.Empty<string>()));
        Assert.Equal(saved, PbpLookRules.Read(PbpLookRules.Write(saved, 60)).Look);
        Assert.Equal("board", PbpLookRules.SlotOf(saved.Board));
        Assert.Equal("companion", PbpLookRules.SlotOf(saved.Companion));
        Assert.Equal(saved, PbpLookRules.With(PbpLookRules.With(ChessLook.Empty, saved.Board!), saved.Companion!));
        var page = new JObject { ["companion"] = JValue.CreateNull() };
        Assert.Equal(saved with { Companion = null }, PbpLookRules.PagePick(page, saved, grants));
        page["board"] = JValue.CreateNull();
        Assert.Equal(ChessLook.Empty, PbpLookRules.PagePick(page, saved, grants));
        Assert.Equal(ChessLook.Empty, PbpLookRules.Normal(new JObject { ["companion"] = "chess.companion.unknown" }));
    }

    [Fact]
    public void Reconcile_newer_server_wins_else_local_is_pushed_when_different()
    {
        var local = new ChessLook("chess.set.royal-capes", null, null);
        var server = new ChessLook(null, "chess.banner.crest", null);
        Assert.Equal((server, true, false), PbpLookRules.Reconcile(local, 100, server, 200));
        Assert.Equal((local, false, true), PbpLookRules.Reconcile(local, 300, server, 200));
        Assert.Equal((local, false, true), PbpLookRules.Reconcile(local, 300, null, 0));
        Assert.Equal((local, false, false), PbpLookRules.Reconcile(local, 300, local, 200));
        // never picked here: nothing to push over the server
        Assert.Equal((ChessLook.Empty, false, false), PbpLookRules.Reconcile(ChessLook.Empty, 0, null, 0));
    }

    [Fact]
    public void Equip_answer_and_body_follow_contract_3()
    {
        var body = JObject.Parse("{\"ok\":true,\"equip\":{\"chess\":{\"set\":\"chess.set.velvet-court\",\"banner\":null,\"insignia\":null},\"desk\":{},\"updatedAt\":1730000000000}}");
        var (look, at) = PbpLookRules.FromEquip(body);
        Assert.Equal(new ChessLook("chess.set.velvet-court", null, null), look);
        Assert.Equal(1730000000000, at);
        Assert.Null(PbpLookRules.FromEquip(JObject.Parse("{\"ok\":true,\"equip\":{\"updatedAt\":5}}")).Look);
        Assert.Equal(0, PbpLookRules.FromEquip(null).UpdatedAt);
        Assert.True(PbpLookRules.StampOf("2026-10-30T10:00:00Z") > 0);

        var put = PbpLookRules.EquipBody("u1", new ChessLook(null, "chess.banner.crest", null));
        Assert.Equal("u1", (string?)put["unified_id"]);
        Assert.Equal("chess", (string?)put["family"]);
        Assert.Equal("chess.banner.crest", (string?)put["value"]!["banner"]);
        Assert.Equal(JTokenType.Null, put["value"]!["set"]!.Type);
    }

    [Fact]
    public void Counter_frame_opens_the_chess_tab()
    {
        Assert.Equal("chess", PbpLookRules.CounterTabOf(JObject.Parse("{\"type\":\"open-prize-counter\",\"tab\":\"chess\"}")));
        Assert.Equal("prime", PbpLookRules.CounterTabOf(JObject.Parse("{\"tab\":\"prime\"}")));
        Assert.Equal("chess", PbpLookRules.CounterTabOf(JObject.Parse("{\"tab\":\"kart\"}")));
        Assert.Equal("chess", PbpLookRules.CounterTabOf(null));
    }

    private static string PageDir()
    {
        return Path.Combine(SourceRoots.RepoRoot, "Assets", "web", "piecebypiece");   // the web trees live in Assets/web
    }

    [Fact]
    public void Page_catalog_matches_the_host_catalog()
    {
        var src = File.ReadAllText(Path.Combine(PageDir(), "skins", "look.js"));
        foreach (var (slot, ids) in PbpLookRules.Catalog)
        {
            if (slot is "finish" or "board")
            {
                var file = slot == "finish" ? Path.Combine("skins", "finish-ids.js") : Path.Combine("board", "theme-ids.js");
                var source = File.ReadAllText(Path.Combine(PageDir(), file));
                var pageIds = Regex.Matches(source, @"'(chess\." + slot + @"\.[a-z]+)'\s*:")
                    .Select(x => x.Groups[1].Value);
                Assert.Equal(ids, pageIds);
                continue;
            }
            var m = Regex.Match(src, slot + @": Object\.freeze\(\[([^\]]*)\]\)");
            Assert.True(m.Success, "look.js has no " + slot + " row");
            var page = Regex.Matches(m.Groups[1].Value, "'([^']+)'").Select(x => x.Groups[1].Value).ToArray();
            Assert.Equal(ids, page);
        }
    }

    [Fact]
    public void Every_attachment_item_ships_art_for_all_six_pieces()
    {
        var reg = JObject.Parse(File.ReadAllText(Path.Combine(PageDir(), "assets", "skins", "registry.json")));
        foreach (var id in new[] { "set", "banner", "insignia" }.SelectMany(slot => PbpLookRules.Catalog[slot]))
            foreach (var piece in new[] { "pawn", "knight", "bishop", "rook", "queen", "king" })
            {
                var row = reg["items"]!.FirstOrDefault(i => (string?)i["id"] == id && (string?)i["piece"] == piece);
                Assert.True(row != null, $"{id} has no {piece}");
                Assert.True(File.Exists(Path.Combine(PageDir(), "assets", "skins", (string)row!["file"]!)), $"{row["file"]} is missing");
            }
    }
}
