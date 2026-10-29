using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Sparkles;

/// <summary>
/// SPARKLES BY PLACE (B1 W5): the results slip. Turns what a claim came to into the one page
/// message every web tree's <c>sparkles-slip.js</c> draws: <c>{ type: "sparkles-receipt", paid,
/// credited, online, taper, fee, reason, shadow, place, of, medal?, outcome?, sp, stamp, gentle,
/// lines }</c>. The HOST localises the lines, so no page carries ten language tables.
/// SENDS NOTHING for a shadow receipt (the rollout day), no claim service, or a refusal the slip
/// has no line for (signed-out hosts get one line; a busy or pending server gets none).
/// </summary>
public static class SparklesSlip
{
    public const string MessageType = "sparkles-receipt";

    /// <summary>Receipt reasons (and refusals) that the slip names, with their loc key.</summary>
    private static readonly Dictionary<string, string> ReasonKeys = new(StringComparer.Ordinal)
    {
        ["short"] = "Sparkles_Slip_Short",
        ["offline"] = "Sparkles_Slip_Offline",
        ["pair"] = "Sparkles_Slip_Pair",
        ["young"] = "Sparkles_Slip_Young",
        ["solo_room"] = "Sparkles_Slip_SoloRoom",
        ["disputed"] = "Sparkles_Slip_Disputed",
        ["flagged"] = "Sparkles_Slip_Flagged",
        ["signin"] = "Sparkles_Slip_SignIn",
    };

    /// <summary>The page message for one claim outcome, or null when the slip shows nothing.</summary>
    public static JObject? Build(SparklesClaimOutcome? outcome, bool gentle, Func<string, string> loc)
    {
        if (outcome == null) return null;
        var r = outcome.Receipt;
        if (r == null)
        {
            // A refusal with no receipt: only the ones the player can act on or should know.
            var why = outcome.Reason ?? "";
            if (why != "ceiling" && !ReasonKeys.ContainsKey(why)) return null;
            return Message(new SparklesReceipt { Reason = why }, gentle, Lines(null, why, loc), loc);
        }
        if (r.Shadow) return null;
        return Message(r, gentle, Lines(r, r.Reason, loc), loc);
    }

    /// <summary>The slip's lines, top to bottom: the result, online, rate, then the reason.</summary>
    public static List<string> Lines(SparklesReceipt? r, string? reason, Func<string, string> loc)
    {
        var lines = new List<string>();
        if (r != null && ResultLine(r, loc) is { } result) lines.Add(result);
        if (r != null && r.Online > 1) lines.Add(Fmt(loc("Sparkles_Slip_Online"), Num(r.Online)));
        var rate = r == null ? null
            : r.Fee ? loc("Sparkles_Slip_FeeOnly")
            : r.Taper > 0 && r.Taper < 1 ? loc("Sparkles_Slip_HalfRate")
            : reason == "taper" ? loc("Sparkles_Slip_HalfRate")
            : null;
        if (rate != null && reason != "ceiling") lines.Add(rate);
        if (reason == "ceiling")
        {
            var hours = r == null || r.ResetInMs <= 0 ? 0 : (int)Math.Ceiling(r.ResetInMs / 3_600_000.0);
            lines.Add(hours > 0 ? Fmt(loc("Sparkles_Slip_Ceiling"), hours) : loc("Sparkles_Slip_CeilingToday"));
        }
        else if (reason != null && ReasonKeys.TryGetValue(reason, out var key))
        {
            lines.Add(loc(key));
        }
        return lines;
    }

    /// <summary>Place 2 of 8, a medal, or a duel outcome. Null when the receipt names none.</summary>
    private static string? ResultLine(SparklesReceipt r, Func<string, string> loc)
    {
        var kind = r.Result?.Value<string?>("kind") ?? r.Kind;
        if (kind == "medal" || r.Result?["medal"] != null)
            return (r.Result?.Value<string?>("medal")) switch
            {
                "gold" => loc("Sparkles_Slip_Gold"),
                "silver" => loc("Sparkles_Slip_Silver"),
                "bronze" => loc("Sparkles_Slip_Bronze"),
                _ => loc("Sparkles_Slip_Finished"),
            };
        if (kind == "duel" || r.Result?["outcome"] != null)
            return (r.Result?.Value<string?>("outcome")) switch
            {
                "win" => loc("Sparkles_Slip_Win"),
                "draw" => loc("Sparkles_Slip_Draw"),
                "loss" => loc("Sparkles_Slip_Loss"),
                _ => null,
            };
        return r.Place > 0 && r.Of > 0 ? Fmt(loc("Sparkles_Slip_Place"), r.Place, r.Of) : null;
    }

    private static JObject Message(SparklesReceipt r, bool gentle, List<string> lines, Func<string, string> loc)
    {
        var o = new JObject
        {
            ["type"] = MessageType,
            ["paid"] = r.Paid,
            ["credited"] = r.Credited,
            ["online"] = r.Online,
            ["taper"] = r.Taper,
            ["fee"] = r.Fee,
            ["reason"] = r.Reason,
            ["shadow"] = false,
            ["place"] = r.Place,
            ["of"] = r.Of,
            ["sp"] = r.Sp,
            ["stamp"] = loc("Sparkles_Slip_Paid"),
            ["gentle"] = gentle,
            ["lines"] = new JArray(lines),
        };
        if (r.Result?.Value<string?>("medal") is { } medal) o["medal"] = medal;
        if (r.Result?.Value<string?>("outcome") is { } duel) o["outcome"] = duel;
        return o;
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Fmt(string template, params object[] args)
    {
        try { return string.Format(CultureInfo.InvariantCulture, template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>
    /// Await a claim and hand its slip to the page (<paramref name="postJson"/> runs on the app's
    /// dispatcher). Never throws; a host discards the task.
    /// </summary>
    public static Task Deliver(Task<SparklesClaimOutcome?>? claim, Action<string> postJson) =>
        DeliverCore(claim, postJson, App.Current?.Dispatcher, o => Build(o, GentleVisuals.On, Loc.Get));

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
