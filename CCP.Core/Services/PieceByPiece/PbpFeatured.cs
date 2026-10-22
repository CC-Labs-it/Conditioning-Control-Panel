using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>The featured match as <c>GET /v2/pbp/featured</c> names it. Names are display names only.</summary>
public sealed record PbpFeaturedMatch(string Id, string White, string Black, int Moves, int Watchers);

/// <summary>One answer of the featured route: a match (or none), and whether an admin pinned it.</summary>
public sealed record PbpFeaturedReply(PbpFeaturedMatch? Match, bool Pinned)
{
    public static readonly PbpFeaturedReply None = new(null, false);
}

/// <summary>What a round came back as. Dark = the route is not deployed (PBP_WATCH off).</summary>
public enum PbpFeaturedOutcome { Ok, Dark, Error }

/// <summary>
/// The featured route's wire, pure. A 200 body must carry a <c>match</c> key (object or null);
/// a match needs a well-formed <c>m_</c> id and both names, or it is no match. A 404 that says
/// <c>not_deployed</c> is Dark; anything else that is not a 200 is an Error.
/// </summary>
public static class PbpFeaturedWire
{
    public const int MaxName = 32;

    private static readonly JsonSerializerSettings ReadSettings = new() { DateParseHandling = DateParseHandling.None };

    public static (PbpFeaturedOutcome Outcome, PbpFeaturedReply Reply) Parse(int status, string? body)
    {
        var o = Json(body);
        if (status == 404 && o != null && (o["error"]?.Type == JTokenType.String) && (string?)o["error"] == "not_deployed")
            return (PbpFeaturedOutcome.Dark, PbpFeaturedReply.None);
        if (status != 200 || o == null || !o.ContainsKey("match")) return (PbpFeaturedOutcome.Error, PbpFeaturedReply.None);

        if (o["match"] is not JObject m) return (PbpFeaturedOutcome.Ok, PbpFeaturedReply.None);
        var id = Str(m["id"]);
        var white = Name(m["white"]);
        var black = Name(m["black"]);
        if (!PbpWatchRules.IsMatchId(id) || white == null || black == null) return (PbpFeaturedOutcome.Ok, PbpFeaturedReply.None);

        var match = new PbpFeaturedMatch(id!, white, black,
            (int)Math.Clamp(Long(m["moves"]), 0, 9999), (int)Math.Clamp(Long(m["watchers"]), 0, 99999));
        bool pinned = o["pinned"]?.Type == JTokenType.Boolean && o.Value<bool>("pinned");
        return (PbpFeaturedOutcome.Ok, new PbpFeaturedReply(match, pinned));
    }

    private static JObject? Json(string? body)
    {
        try { return JsonConvert.DeserializeObject<JToken>(body ?? "", ReadSettings) as JObject; }
        catch { return null; }
    }

    /// <summary>A seat's display name: a string, or an object with display_name / name. Trimmed and capped.</summary>
    private static string? Name(JToken? t)
    {
        var s = t is JObject o ? Str(o["display_name"]) ?? Str(o["name"]) : Str(t);
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length > MaxName ? s.Substring(0, MaxName) : s;
    }

    private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

    private static long Long(JToken? t) => t?.Type switch
    {
        JTokenType.Integer => t.Value<long>(),
        JTokenType.Float => (long)t.Value<double>(),
        _ => 0,
    };
}

/// <summary>When the featured poll runs, pure. Every 20 s while the board is on screen, doubling
/// to two minutes after a failed round; a dark route is asked again only every ten minutes.</summary>
public static class PbpFeaturedPoll
{
    public static readonly TimeSpan Base = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Dark = TimeSpan.FromMinutes(10);

    /// <summary>A failed round keeps the last match on the board only this long after the last good answer.</summary>
    public static readonly TimeSpan KeepOnError = TimeSpan.FromSeconds(60);

    /// <summary>The board is on screen: the Home card host is visible and the window is not minimised.</summary>
    public static bool ShouldRun(bool boardVisible, bool minimized) => boardVisible && !minimized;

    public static TimeSpan Next(TimeSpan current, PbpFeaturedOutcome outcome) => outcome switch
    {
        PbpFeaturedOutcome.Ok => Base,
        PbpFeaturedOutcome.Dark => Dark,
        _ => TimeSpan.FromTicks(Math.Min(Max.Ticks, Math.Max(Base.Ticks, current.Ticks) * 2)),
    };

    /// <summary>The reply the board shows after a round.</summary>
    public static PbpFeaturedReply After(PbpFeaturedReply last, PbpFeaturedOutcome outcome, PbpFeaturedReply fresh, TimeSpan sinceLastOk) =>
        outcome switch
        {
            PbpFeaturedOutcome.Ok => fresh,
            PbpFeaturedOutcome.Dark => PbpFeaturedReply.None,
            _ => sinceLastOk <= KeepOnError ? last : PbpFeaturedReply.None,
        };
}
