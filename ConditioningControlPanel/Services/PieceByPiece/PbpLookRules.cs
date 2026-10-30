using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// The chess wardrobe's rules (7.2 shop, lane B3-C1), pure so they are tested. A look is three
/// slots, each one Prize Counter item id or null: <c>{ set, banner, insignia }</c>. The page draws
/// the art (piecebypiece/skins/look.js holds the same catalog); this side clamps every look to the
/// account's grants before it reaches the page or the server, and words the frames.
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
    };

    public static readonly string[] Slots = { "set", "banner", "insignia" };

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

    /// <summary>Any token to a whole look: unknown ids, wrong slots and junk become null.</summary>
    public static ChessLook Normal(JToken? raw)
    {
        string? Read(string slot)
        {
            var v = raw is JObject o && o[slot]?.Type == JTokenType.String ? (string?)o[slot] : null;
            return v != null && Catalog[slot].Contains(v, StringComparer.Ordinal) ? v : null;
        }
        return new ChessLook(Read("set"), Read("banner"), Read("insignia"));
    }

    /// <summary>The look with every slot the grants do not own taken off. Prime items stay usable
    /// after Prime ends (owner, 2026-10-30): the grant is the whole test.</summary>
    public static ChessLook Clamp(ChessLook look, IEnumerable<string>? grants)
    {
        var held = new HashSet<string>(grants ?? Array.Empty<string>(), StringComparer.Ordinal);
        string? Keep(string? id) => id != null && held.Contains(GrantOf(id)) ? id : null;
        return new ChessLook(Keep(look.Set), Keep(look.Banner), Keep(look.Insignia));
    }

    public static JObject LookFrame(ChessLook look) => new() { ["type"] = LookType, ["look"] = look.ToJObject() };

    /// <summary>The counter tab a page asked for; the chess page only ever means its own.</summary>
    public static string CounterTabOf(JObject? m)
    {
        var tab = m?["tab"]?.Type == JTokenType.String ? (string?)m["tab"] : null;
        return tab is "chess" or "prime" ? tab : CounterTab;
    }

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

    public static string Write(ChessLook look, long at)
    {
        var o = look.ToJObject();
        o["at"] = at;
        return o.ToString(Formatting.None);
    }

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
internal readonly record struct ChessLook(string? Set, string? Banner, string? Insignia)
{
    public static ChessLook Empty => new(null, null, null);

    public JObject ToJObject() => new()
    {
        ["set"] = Set is null ? JValue.CreateNull() : Set,
        ["banner"] = Banner is null ? JValue.CreateNull() : Banner,
        ["insignia"] = Insignia is null ? JValue.CreateNull() : Insignia,
    };
}
