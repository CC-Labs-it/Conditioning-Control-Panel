using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Sparkles;

/// <summary>
/// SPARKLES BY PLACE (B1 W2): the one call a game host makes when a game ends, and the pure maps
/// from each game's own grading to a medal. The server owns every number; a host only says what
/// happened. Fire and forget: a claim never blocks or fails a host's end-of-game path, and the
/// returned task is there for the results slip (W5) to await.
/// Medals: gold / silver / bronze = places 1-3 of a field of 4, "none" = finished (the fee).
/// </summary>
public static class SparklesGameEnd
{
    public static Func<SparklesClaimService?>? Service { get; set; }
    /// <summary>Arcademy class grade: S+ and S gold, A silver, B bronze, C and pass (zen) finished.</summary>
    public static string ArcademyMedal(string? grade) => (grade ?? "").Trim().ToUpperInvariant() switch
    {
        "S+" or "S" => "gold",
        "A" => "silver",
        "B" => "bronze",
        _ => "none",
    };

    /// <summary>A score against the game's own ceiling for that run (the DtRH and Caucus XP caps,
    /// which already scale with length and difficulty): at the cap gold, 60% silver, 30% bronze.</summary>
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

    /// <summary>Claim a finished game. Never throws; null when there is no claim service.
    /// <paramref name="proof"/> rides only on an online claim (the server's own record of it).</summary>
    public static Task<SparklesClaimOutcome?> Claim(string game, string mode, SparklesResult result,
        TimeSpan duration, JObject? detail = null, JObject? proof = null, SparklesClaimService? svc = null)
    {
        svc ??= Service?.Invoke();
        if (svc == null) return Task.FromResult<SparklesClaimOutcome?>(null);
        SparklesClaim claim;
        try { claim = SparklesClaim.Create(game, mode, result, (long)Math.Max(0, duration.TotalMilliseconds), detail, proof); }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Sparkles: claim not built for {Game}: {E}", game, ex.Message);
            return Task.FromResult<SparklesClaimOutcome?>(null);
        }
        return Task.Run<SparklesClaimOutcome?>(async () =>
        {
            try
            {
                var outcome = await svc.ClaimAsync(claim).ConfigureAwait(false);
                var r = outcome.Receipt;
                Serilog.Log.Information("Sparkles: {Game}/{Mode} paid={Paid} credited={Credited} reason={Reason}{Shadow}",
                    game, mode, r?.Paid ?? 0, r?.Credited ?? 0, r?.Reason ?? outcome.Reason ?? "-", r?.Shadow == true ? " shadow" : "");
                return outcome;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("Sparkles: claim for {Game} failed: {E}", game, ex.Message);
                return null;
            }
        });
    }
}
