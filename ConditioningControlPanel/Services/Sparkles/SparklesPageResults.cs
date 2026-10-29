using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Sparkles;

/// <summary>A page's end-of-game message, read into the claim its host posts.</summary>
public sealed record SparklesPageClaim(string Game, string Mode, SparklesResult Result, TimeSpan Duration,
    JObject? Detail = null, JObject? Proof = null);

/// <summary>
/// SPARKLES BY PLACE (B1 W3): the games whose pages tell the host how a game ended. Each reader
/// takes the page's frame as untrusted (any field may be missing or the wrong type) and answers the
/// claim, or null when nothing is claimed: a watched or two-players-here chess game, a game someone
/// left, a skipped Breakout board. The server checks every claim again; online truth comes
/// from its own record (the proof), never from the page.
/// </summary>
public static class SparklesPageResults
{
    private static readonly Regex GoonCode = new("^[A-Z0-9]{4,12}$", RegexOptions.CultureInvariant);
    private static readonly Regex MatchId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    private const double MaxGameMs = 6 * 3600_000.0;

    /// <summary>Goon end reasons (core/contracts.js GoonEndReason).</summary>
    public const int GoonSuddenDeath = 1, GoonDraw = 3;

    /// <summary>Piece by Piece <c>pbp:game-over { mode, outcome, plies, durationMs, reason, matchId }</c>.
    /// vs Computer = solo; an online or tournament game = online with the pbp match id as proof.</summary>
    public static SparklesPageClaim? Chess(JObject? o)
    {
        if (o == null) return null;
        var mode = Str(o["mode"]);
        if (mode is not ("solo" or "online")) return null;
        var outcome = Str(o["outcome"]);
        if (outcome is not ("win" or "draw" or "loss")) return null;
        var reason = Str(o["reason"])?.ToLowerInvariant() ?? "";
        if (reason.Contains("abandon") || reason.Contains("abort")) return null;
        var rawPlies = Num(o["plies"]);
        int plies = double.IsNaN(rawPlies) ? 0 : (int)Math.Clamp(rawPlies, 0, 10_000);
        var detail = new JObject { ["plies"] = plies };
        var length = Ms(o["durationMs"]);
        if (mode == "solo") return new("chess", "solo", SparklesResult.ForDuel(outcome), length, detail);
        var id = Str(o["matchId"]);
        if (id == null || !MatchId.IsMatch(id)) return null;
        return new("chess", "online", SparklesResult.ForDuel(outcome), length, detail, new JObject { ["matchId"] = id });
    }

    /// <summary>Goon <c>match-result { practice, code, result: { endReason, winnerIsHost, localIsHost,
    /// survivedMs, disputed, countsForLedger } }</c>, sent once the result is final. Like the server,
    /// only a match that ran its course claims: sudden death or a draw. Mercy (where Escape lands), a
    /// leave and a pre-Live cancel claim nothing; a disputed practice claims nothing either.</summary>
    public static SparklesPageClaim? Goon(JObject? o)
    {
        if (o?["result"] is not JObject r) return null;
        if (r["countsForLedger"]?.Type == JTokenType.Boolean && !(bool)r["countsForLedger"]!) return null;
        var end = Num(r["endReason"]);
        string outcome;
        if (end == GoonDraw) outcome = "draw";
        else if (end == GoonSuddenDeath && Bool(r["winnerIsHost"]) is bool hostWon && Bool(r["localIsHost"]) is bool amHost)
            outcome = hostWon == amHost ? "win" : "loss";
        else return null;
        var length = Ms(r["survivedMs"]);
        if (Bool(o["practice"]) == true)
            return Bool(r["disputed"]) == true ? null
                : new("goon", "practice", SparklesResult.ForDuel(outcome), length);
        var code = Str(o["code"]);
        if (code == null || !GoonCode.IsMatch(code)) return null;
        return new("goon", "online", SparklesResult.ForDuel(outcome), length, null, new JObject { ["goonMatchId"] = code });
    }

    /// <summary>Breakout <c>breakout-wall { walls, ballsLost, wallMs, skipped }</c>: one claim per
    /// cleared wall, medal by the balls lost on it. The Story Demo pays like the full game (same rate
    /// for every tier, the daily taper caps replays); a skipped board claims nothing.</summary>
    public static SparklesPageClaim? BreakoutWall(JObject? o)
    {
        if (o == null || Bool(o["skipped"]) == true) return null;
        var lost = Num(o["ballsLost"]);
        if (double.IsNaN(lost)) return null;
        int ballsLost = (int)Math.Clamp(lost, 0, 999);
        return new("breakout", "solo", SparklesResult.ForMedal(SparklesGameEnd.BreakoutMedal(ballsLost)),
            Ms(o["wallMs"]), new JObject { ["ballsLost"] = ballsLost });
    }

    /// <summary>Post a read claim (fire and forget); a null claim is a no-op.</summary>
    public static Task<SparklesClaimOutcome?> Send(SparklesPageClaim? c) => c == null
        ? Task.FromResult<SparklesClaimOutcome?>(null)
        : SparklesGameEnd.Claim(c.Game, c.Mode, c.Result, c.Duration, c.Detail, c.Proof);

    private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

    private static bool? Bool(JToken? t) => t?.Type == JTokenType.Boolean ? (bool)t : null;

    private static double Num(JToken? t)
    {
        if (t == null || t.Type is not (JTokenType.Integer or JTokenType.Float)) return double.NaN;
        var d = (double)t;
        return double.IsFinite(d) ? d : double.NaN;
    }

    private static TimeSpan Ms(JToken? t)
    {
        var ms = Num(t);
        return double.IsNaN(ms) ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, MaxGameMs));
    }
}
