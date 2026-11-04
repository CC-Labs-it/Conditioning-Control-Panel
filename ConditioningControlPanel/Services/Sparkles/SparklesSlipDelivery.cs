using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Sparkles;

public static class SparklesSlipDelivery
{
    /// <summary>
    /// Await a claim and hand its slip to the page (<paramref name="postJson"/> runs on the app's
    /// dispatcher). Never throws; a host discards the task.
    /// </summary>
    public static Task Deliver(Task<SparklesClaimOutcome?>? claim, Action<string> postJson) =>
        DeliverCore(claim, postJson, App.Current?.Dispatcher,
            o => SparklesSlip.WithWallet(SparklesSlip.Build(o, GentleVisuals.On, Loc.Get), App.Settings?.Current?.SkillPoints));

    internal static async Task DeliverCore(Task<SparklesClaimOutcome?>? claim, Action<string> postJson,
        Dispatcher? dispatcher, Func<SparklesClaimOutcome?, JObject?> build)
    {
        if (claim == null || postJson == null) return;
        try
        {
            var outcome = await claim.ConfigureAwait(false);
            void Post()
            {
                try
                {
                    if (build(outcome) is { } msg) postJson(msg.ToString(Formatting.None));
                }
                catch (Exception ex) { App.Logger?.Debug("Sparkles: slip not posted: {E}", ex.Message); }
            }
            if (dispatcher == null || dispatcher.CheckAccess()) { Post(); return; }
            if (dispatcher.HasShutdownStarted) return;
            _ = dispatcher.BeginInvoke(new Action(Post));
        }
        catch (Exception ex) { App.Logger?.Debug("Sparkles: slip failed: {E}", ex.Message); }
    }
}
