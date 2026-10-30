using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.GameRewards;
public sealed record SparklesPageClaim(string Game, string Mode, SparklesResult Result, TimeSpan Duration,
    JObject? Detail = null, JObject? Proof = null);
public static class SparklesPageResults
{
    private static readonly Regex GoonCode = new("^[A-Z0-9]{4,12}$", RegexOptions.CultureInvariant);
    private static readonly Regex MatchId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    private const double MaxGameMs = 6 * 3600_000.0;
    public const int GoonSuddenDeath = 1, GoonDraw = 3;
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
    public static SparklesPageClaim? BreakoutWall(JObject? o)
    {
        if (o == null || Bool(o["skipped"]) == true) return null;
        var lost = Num(o["ballsLost"]);
        if (double.IsNaN(lost)) return null;
        int ballsLost = (int)Math.Clamp(lost, 0, 999);
        return new("breakout", "solo", SparklesResult.ForMedal(SparklesGameEnd.BreakoutMedal(ballsLost)),
            Ms(o["wallMs"]), new JObject { ["ballsLost"] = ballsLost });
    }
    public static Task<SparklesClaimOutcome?> Send(SparklesPageClaim? c, SparklesClaimService? service = null) => c == null
        ? Task.FromResult<SparklesClaimOutcome?>(null)
        : SparklesGameEnd.Claim(c.Game, c.Mode, c.Result, c.Duration, c.Detail, c.Proof, service);
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
