using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Sparkles.Claims;

/// <summary>
/// WPF Services/Sparkles/SparklesGameEnd.cs (SPARKLES BY PLACE, B1 W2) on the Core claim service: the
/// one call a game host makes when a game ends, and the pure maps from each game's own grading to a
/// medal. The server owns every number; a host only says what happened. Fire and forget: a claim
/// never blocks or fails a host's end-of-game path; the task is there for the results slip.
/// </summary>
public static class SparklesGameEnd
{
    /// <summary>A score against the game's own ceiling for that run (the DtRH and Caucus XP caps):
    /// at the cap gold, 60% silver, 30% bronze.</summary>
    public static string RatioMedal(double score, double cap)
    {
        if (!(cap > 0) || double.IsNaN(score)) return "none";
        var r = score / cap;
        return r >= 1.0 ? "gold" : r >= 0.6 ? "silver" : r >= 0.3 ? "bronze" : "none";
    }

    /// <summary>Intake: top marks (the certificate's line) gold, 70% silver, 50% bronze.</summary>
    public static string IntakeMedal(double total, double max, double topMarksPercent)
    {
        if (!(max > 0)) return "none";
        var pct = total / max * 100.0;
        return pct >= topMarksPercent ? "gold" : pct >= 70 ? "silver" : pct >= 50 ? "bronze" : "none";
    }

    /// <summary>Breakout, per cleared wall: no ball lost gold, one silver, two bronze, more finished.</summary>
    public static string BreakoutMedal(int ballsLost) => ballsLost switch
    {
        <= 0 => "gold",
        1 => "silver",
        2 => "bronze",
        _ => "none",
    };

    /// <summary>Claim a finished game. Never throws; null when there is no claim service (signed out,
    /// sandbox). <paramref name="proof"/> rides only on an online claim.</summary>
    public static Task<SparklesClaimOutcome?> Claim(SparklesClaimService? svc, string game, string mode,
        SparklesResult result, TimeSpan duration, JObject? detail = null, JObject? proof = null)
    {
        if (svc == null) return Task.FromResult<SparklesClaimOutcome?>(null);
        SparklesClaim claim;
        try { claim = SparklesClaim.Create(game, mode, result, (long)Math.Max(0, duration.TotalMilliseconds), detail, proof); }
        catch (Exception ex)
        {
            Log.Debug("Sparkles: claim not built for {Game}: {E}", game, ex.Message);
            return Task.FromResult<SparklesClaimOutcome?>(null);
        }
        return Task.Run<SparklesClaimOutcome?>(async () =>
        {
            try
            {
                var outcome = await svc.ClaimAsync(claim).ConfigureAwait(false);
                var r = outcome.Receipt;
                Log.Information("Sparkles: {Game}/{Mode} paid={Paid} credited={Credited} reason={Reason}{Shadow}",
                    game, mode, r?.Paid ?? 0, r?.Credited ?? 0, r?.Reason ?? outcome.Reason ?? "-", r?.Shadow == true ? " shadow" : "");
                return outcome;
            }
            catch (Exception ex)
            {
                Log.Debug("Sparkles: claim for {Game} failed: {E}", game, ex.Message);
                return null;
            }
        });
    }
}

/// <summary>A page's end-of-game message, read into the claim its host posts.</summary>
public sealed record SparklesPageClaim(string Game, string Mode, SparklesResult Result, TimeSpan Duration,
    JObject? Detail = null, JObject? Proof = null);

/// <summary>
/// WPF Services/Sparkles/SparklesPageResults.cs (B1 W3) for the pages this head hosts: chess and
/// Breakout (no Goon host here yet). Each reader takes the page's frame as untrusted and answers the
/// claim, or null when nothing is claimed. The server checks every claim again.
/// </summary>
public static class SparklesPageResults
{
    private static readonly Regex MatchId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    private const double MaxGameMs = 6 * 3600_000.0;

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

    /// <summary>Breakout <c>breakout-wall { walls, ballsLost, wallMs, skipped }</c>: one claim per cleared
    /// wall, medal by the balls lost on it. The Story Demo pays like the full game; a skipped board claims nothing.</summary>
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
    public static Task<SparklesClaimOutcome?> Send(SparklesPageClaim? c, SparklesClaimService? svc) => c == null
        ? Task.FromResult<SparklesClaimOutcome?>(null)
        : SparklesGameEnd.Claim(svc, c.Game, c.Mode, c.Result, c.Duration, c.Detail, c.Proof);

    /// <summary>
    /// WPF GlassroadRules.SparklesClaimFor: the claim a Racing Thoughts 3D <c>race-result</c> frame earns
    /// (game <c>rt3d</c>), or null (unfinished, flagged, malformed, a short Time Trial, an online result
    /// with no room). The server reads an online room's signed result and ignores the client place.
    /// </summary>
    public static SparklesPageClaim? Glassroad(JObject? m)
    {
        if (m == null || (string?)m["type"] != "race-result") return null;
        if (m["finished"]?.Type != JTokenType.Boolean || !(bool)m["finished"]!) return null;
        if (m["flagged"]?.Type == JTokenType.Boolean && (bool)m["flagged"]!) return null;
        int laps = ReadInt(m, "laps"), fullLaps = ReadInt(m, "fullLaps"), place = ReadInt(m, "place"), of = ReadInt(m, "of");
        double timeS = m["timeS"]?.Type is JTokenType.Float or JTokenType.Integer ? (double)m["timeS"]! : 0;
        if (laps < 1 || !(timeS > 0) || double.IsInfinity(timeS)) return null;
        bool fullLength = fullLaps > 0 && laps == fullLaps;
        bool placed = of >= 1 && place >= 1 && place <= of;
        var detail = new JObject { ["laps"] = fullLaps > 0 ? Math.Min(laps, fullLaps) : laps };
        if (m["track"]?.Type == JTokenType.String) detail["track"] = (string?)m["track"];
        var duration = TimeSpan.FromSeconds(Math.Min(timeS, TimeSpan.FromDays(1).TotalSeconds));
        switch ((string?)m["mode"] ?? "race")
        {
            case "race":
                return placed ? new("rt3d", "solo", SparklesResult.ForPlace(place, of), duration, detail) : null;
            case "trial":
                if (!fullLength) return null;
                var medal = ((string?)m["medal"] ?? "") switch
                {
                    "glass" or "gold" => "gold",
                    "silver" => "silver",
                    "bronze" => "bronze",
                    _ => "none",
                };
                return new("rt3d", "trial", SparklesResult.ForMedal(medal), duration, detail);
            case "ghost":
                var outcome = (string?)m["ghost"];
                if (!fullLength || outcome is not ("win" or "loss")) return null;
                return new("rt3d", "ghost", SparklesResult.ForDuel(outcome), duration, detail);
            case "online":
                var roomId = m["roomId"]?.Type == JTokenType.String ? (string?)m["roomId"] : null;
                if (!placed || !IsRoomId(roomId)) return null;
                return new("rt3d", "online", SparklesResult.ForPlace(place, of), duration, detail,
                    new JObject { ["roomId"] = roomId });
            default:
                return null;
        }
    }

    private static bool IsRoomId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        foreach (var c in id)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')) return false;
        return true;
    }

    private static int ReadInt(JObject m, string key) =>
        m[key] is JValue { Type: JTokenType.Integer or JTokenType.Float } v && v.Value<double>() is var d
            && d >= 0 && d <= 10_000 && d == Math.Floor(d) ? (int)d : 0;

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
