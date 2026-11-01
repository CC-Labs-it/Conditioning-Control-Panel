using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Sparkles;

public static class SparklesSlip
{
    public const string MessageType = GameRewards.SparklesSlip.MessageType;
    public static JObject? Build(SparklesClaimOutcome? outcome, bool gentle, Func<string, string> loc) => GameRewards.SparklesSlip.Build(outcome, gentle, loc);
    public static List<string> Lines(SparklesReceipt? r, string? reason, Func<string, string> loc) => GameRewards.SparklesSlip.Lines(r, reason, loc);

    /// <summary>
    /// Await a claim and hand its slip to the page (<paramref name="postJson"/> runs on the app's
    /// dispatcher). Never throws; a host discards the task.
    /// </summary>
    public static Task Deliver(Task<SparklesClaimOutcome?>? claim, Action<string> postJson) =>
        DeliverCore(claim, postJson, App.Current?.Dispatcher,
            o => WithWallet(Build(o, GentleVisuals.On, Loc.Get), App.Settings?.Current?.SkillPoints));

    /// <summary>The chip counts up to the wallet the app shows: the server's balance can sit below it
    /// (raise-only adoption keeps unsynced local credit), so the higher of the two wins.</summary>
    internal static JObject? WithWallet(JObject? msg, int? local)
    {
        if (msg != null && local is int sp && msg["sp"]?.Type == JTokenType.Integer && sp > (long)msg["sp"]!)
            msg["sp"] = sp;
        return msg;
    }

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
