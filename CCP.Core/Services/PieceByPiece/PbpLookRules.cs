using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// A chess look has independent slots for clothes, a banner, an insignia, a finish, a board and a
/// cheerleader. Each stores a Prize Counter item id or null. The page draws the art and the host checks
/// the account's grants before sending the player's choice to the page or server.
///
/// <para>Frames (piecebypiece/skins/wear.js):
/// <list type="bullet">
/// <item>host -&gt; page <c>{ type: 'owned', grants: [cos.chess.*, cos.prime.banner.arena] }</c> at boot,
///   on every ownership change and when the Prize Counter closes (contract 1)</item>
/// <item>host -&gt; page <c>{ type: 'pbp:look', look }</c> at boot and after an adopted server copy</item>
/// <item>page -&gt; host <c>{ type: 'pbp:look', look }</c> on a pick (stored, then PUT /v2/equip, contract 3)</item>
/// <item>page -&gt; host <c>{ type: 'open-prize-counter', tab, from }</c> (contract 1)</item>
/// </list></para>
/// </summary>
internal static class PbpLookRules
{
    public const string OwnedType = "owned";
    public const string LookType = "pbp:look";
    public const string CounterType = "open-prize-counter";
    public const string CounterFrom = "piecebypiece";
    public const string CounterTab = "chess";

    /// <summary>Every item the board can draw, by slot (piecebypiece/skins/look.js CATALOG).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Catalog = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["set"] = new[] { "chess.set.royal-capes", "chess.set.velvet-court" },
        ["banner"] = new[] { "chess.banner.house-pink", "chess.banner.crest", "prime.banner.arena" },
        ["insignia"] = new[] { "chess.insignia.heart", "chess.insignia.star" },
        ["finish"] = new[] { "chess.finish.polka", "chess.finish.marble", "chess.finish.contour", "chess.finish.opal", "chess.finish.kintsugi" },
        ["board"] = new[] { "chess.board.observatory", "chess.board.conservatory", "chess.board.theatre", "chess.board.arcade" },
        ["companion"] = new[] { "chess.companion.emi" },
    };

    public static readonly string[] Slots = { "set", "banner", "insignia", "finish", "board", "companion" };

    public static string GrantOf(string itemId) => "cos." + itemId;

    /// <summary>The grants the page reads: catalog items only, sorted.</summary>
    public static JArray ChessGrants(IEnumerable<string>? grants)
    {
        var held = new HashSet<string>(grants ?? Array.Empty<string>(), StringComparer.Ordinal);
        var rows = new JArray();
        foreach (var id in Catalog.Values.SelectMany(v => v).Select(GrantOf).OrderBy(g => g, StringComparer.Ordinal))
            if (held.Contains(id)) rows.Add(id);
        return rows;
    }

    public static JObject OwnedFrame(IEnumerable<string>? grants)
        => new() { ["type"] = OwnedType, ["grants"] = ChessGrants(grants) };

    public const string StockType = "pbp:stock";

    /// <summary>host -&gt; page <c>{ type: 'pbp:stock', later: [{ id, from: 'YYYY-MM-DD' }] }</c>: the chess rows on
    /// sale but out of this week's Prize Counter stock, with the Monday they are back, read from a
    /// <c>/v2/shop/state</c> body's chess tab <c>later</c> (CCP-Server shop.js, ccp-bugs #1738). Anything that is
    /// not a chess id with a plain date is dropped; an older server (no <c>later</c>) gives an empty list.</summary>
    public static JObject StockFrame(JToken? shopState)
    {
        var later = new JArray();
        var tabs = shopState is JObject s ? s["tabs"] as JArray : null;
        var chess = tabs?.OfType<JObject>().FirstOrDefault(t => t["id"]?.Type == JTokenType.String && (string?)t["id"] == CounterTab);
        if (chess?["later"] is JArray rows)
        {
            foreach (var row in rows.OfType<JObject>())
            {
                var id = row["id"]?.Type == JTokenType.String ? (string?)row["id"] : null;
                var from = row["from"]?.Type == JTokenType.String ? (string?)row["from"] : null;
                if (id == null || id.Length > 80 || !id.StartsWith("chess.", StringComparison.Ordinal)) continue;
                if (from == null || !System.Text.RegularExpressions.Regex.IsMatch(from, @"^\d{4}-\d{2}-\d{2}$")) continue;
                later.Add(new JObject { ["id"] = id, ["from"] = from });
            }
        }
        return new JObject { ["type"] = StockType, ["later"] = later };
    }

    /// <summary>Any token to a whole look: unknown ids, wrong slots and junk become null.</summary>
    public static ChessLook Normal(JToken? raw)
    {
        string? Read(string slot)
        {
            var v = raw is JObject o && o[slot]?.Type == JTokenType.String ? (string?)o[slot] : null;
            return v != null && Catalog[slot].Contains(v, StringComparer.Ordinal) ? v : null;
        }
        return new ChessLook(Read("set"), Read("banner"), Read("insignia"), Read("finish"), Read("board"), Read("companion"));
    }

    /// <summary>An older page can change clothes without clearing a finish it cannot see.</summary>
    public static ChessLook PagePick(JObject raw, ChessLook saved, IEnumerable<string>? grants)
    {
        var pick = Normal(raw);
        if (!raw.ContainsKey("finish")) pick = pick with { Finish = saved.Finish };
        if (!raw.ContainsKey("board")) pick = pick with { Board = saved.Board };
        if (!raw.ContainsKey("companion")) pick = pick with { Companion = saved.Companion };
        return Clamp(pick, grants);
    }

    /// <summary>The look with every slot the grants do not own taken off. Prime items stay usable
    /// after Prime ends (owner, 2026-10-30): the grant is the whole test.</summary>
    public static ChessLook Clamp(ChessLook look, IEnumerable<string>? grants)
    {
        var held = new HashSet<string>(grants ?? Array.Empty<string>(), StringComparer.Ordinal);
        string? Keep(string? id) => id != null && held.Contains(GrantOf(id)) ? id : null;
        return new ChessLook(Keep(look.Set), Keep(look.Banner), Keep(look.Insignia), Keep(look.Finish), Keep(look.Board), Keep(look.Companion));
    }

    public static JObject LookFrame(ChessLook look) => new() { ["type"] = LookType, ["look"] = look.ToJObject() };

    /// <summary>The counter tab a page asked for; the chess page only ever means its own.</summary>
    public static string CounterTabOf(JObject? m)
    {
        var tab = m?["tab"]?.Type == JTokenType.String ? (string?)m["tab"] : null;
        return tab is "chess" or "prime" ? tab : CounterTab;
    }

    public static bool CounterHandsFocusBack(string reason)
        => reason != PrizeCounter.PrizeCounterRules.ClosePanic && reason != PrizeCounter.PrizeCounterRules.CloseAppExit
           && reason != PrizeCounter.PrizeCounterRules.CloseFailed;

    // ---------------------------------------------------------------- the stored copy

    /// <summary>The saved pick and when it was made (unix ms), as AppSettings.ChessLook holds it.</summary>
    public static (ChessLook Look, long At) Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (ChessLook.Empty, 0);
        try
        {
            var o = JObject.Parse(json);
            long at = o["at"]?.Type == JTokenType.Integer ? (long)o["at"]! : 0;
            return (Normal(o), Math.Max(0, at));
        }
        catch (JsonException) { return (ChessLook.Empty, 0); }
    }

    public static string Write(ChessLook look, long at, bool owed = false)
    {
        var o = look.ToJObject();
        o["at"] = at;
        if (owed) o["owed"] = true;
        return o.ToString(Formatting.None);
    }

    /// <summary>True while the saved pick has not reached <c>/v2/equip</c> yet (the PUT failed or never
    /// answered). <c>updatedAt</c> is shared by every family, so a newer Rain or desk write would
    /// otherwise make the server's older chess look win at the next launch.</summary>
    public static bool IsOwed(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { return JObject.Parse(json)["owed"] is JValue { Type: JTokenType.Boolean } v && (bool)v; }
        catch (JsonException) { return false; }
    }

    /// <summary>Launch with a push owed: the local pick wins and is PUT again, whatever the server holds.</summary>
    public static (ChessLook Look, bool Adopt, bool Push) Reconcile(ChessLook local, long localAt, ChessLook? server, long serverAt, bool owed)
        => owed ? (local, false, true) : Reconcile(local, localAt, server, serverAt);

    /// <summary>The counter's Wear on a chess row: the slot that item goes in, or null when the board
    /// does not draw it.</summary>
    public static string? SlotOf(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        foreach (var (slot, ids) in Catalog)
            if (ids.Contains(itemId, StringComparer.Ordinal)) return slot;
        return null;
    }

    /// <summary>The look with one item put on its slot (an id the board does not draw leaves it as it was).</summary>
    public static ChessLook With(ChessLook look, string itemId) => SlotOf(itemId) switch
    {
        "set" => look with { Set = itemId },
        "banner" => look with { Banner = itemId },
        "insignia" => look with { Insignia = itemId },
        "finish" => look with { Finish = itemId },
        "board" => look with { Board = itemId },
        "companion" => look with { Companion = itemId },
        _ => look,
    };

    /// <summary>
    /// Which copy wins at launch (contract 3): the server's when its <c>updatedAt</c> is newer than
    /// the local pick, else the local one. <c>Push</c> says the local copy should be PUT because the
    /// server holds something older and different.
    /// </summary>
    public static (ChessLook Look, bool Adopt, bool Push) Reconcile(ChessLook local, long localAt, ChessLook? server, long serverAt)
    {
        if (server is { } s && serverAt > localAt) return (s, !s.Equals(local), false);
        bool differs = server is not { } sv || !sv.Equals(local);
        return (local, false, differs && localAt > 0);
    }

    /// <summary>The chess slice of a <c>GET /v2/equip</c> answer, or null when there is none.</summary>
    public static (ChessLook? Look, long UpdatedAt) FromEquip(JObject? body)
    {
        var equip = body?["equip"] as JObject;
        if (equip == null) return (null, 0);
        return (equip["chess"] is JObject c ? Normal(c) : null, StampOf(equip["updatedAt"]));
    }

    /// <summary><c>updatedAt</c> as unix ms: a number as is, an ISO date parsed, anything else 0.</summary>
    public static long StampOf(JToken? t)
    {
        if (t == null) return 0;
        if (t.Type == JTokenType.Integer) return Math.Max(0, (long)t);
        if (t.Type == JTokenType.Float) return Math.Max(0, (long)(double)t);
        if (t.Type == JTokenType.Date) return Math.Max(0, new DateTimeOffset((DateTime)t).ToUnixTimeMilliseconds());
        if (t.Type == JTokenType.String && DateTimeOffset.TryParse((string?)t, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d)) return Math.Max(0, d.ToUnixTimeMilliseconds());
        return 0;
    }

    /// <summary>The <c>PUT /v2/equip</c> body for the chess family.</summary>
    public static JObject EquipBody(string unifiedId, ChessLook look) => new()
    {
        ["unified_id"] = unifiedId,
        ["family"] = "chess",
        ["value"] = look.ToJObject(),
    };
}

/// <summary>One look: each slot an item id or null.</summary>
internal readonly record struct ChessLook(string? Set, string? Banner, string? Insignia, string? Finish = null,
    string? Board = null, string? Companion = null)
{
    public static ChessLook Empty => new(null, null, null);

    public JObject ToJObject() => new()
    {
        ["set"] = Set is null ? JValue.CreateNull() : Set,
        ["banner"] = Banner is null ? JValue.CreateNull() : Banner,
        ["insignia"] = Insignia is null ? JValue.CreateNull() : Insignia,
        ["finish"] = Finish is null ? JValue.CreateNull() : Finish,
        ["board"] = Board is null ? JValue.CreateNull() : Board,
        ["companion"] = Companion is null ? JValue.CreateNull() : Companion,
    };
}
