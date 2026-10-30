using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.GameRewards;
public static class SparklesGameEnd
{
    public static string BreakoutMedal(int ballsLost) => ballsLost switch
    {
        <= 0 => "gold",
        1 => "silver",
        2 => "bronze",
        _ => "none",
    };
    public static Task<SparklesClaimOutcome?> Claim(string game, string mode, SparklesResult result,
        TimeSpan duration, JObject? detail = null, JObject? proof = null, SparklesClaimService? svc = null)
    {
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
