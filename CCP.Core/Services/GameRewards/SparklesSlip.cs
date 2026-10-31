using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Services.Sparkles;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.GameRewards;
public static class SparklesSlip
{
    public const string MessageType = "sparkles-receipt";
    private static readonly Dictionary<string, string> ReasonKeys = new(StringComparer.Ordinal)
    {
        ["short"] = "Sparkles_Slip_Short",
        ["offline"] = "Sparkles_Slip_Offline",
        ["unreachable"] = "Sparkles_Slip_Unreachable",
        ["pair"] = "Sparkles_Slip_Pair",
        ["young"] = "Sparkles_Slip_Young",
        ["solo_room"] = "Sparkles_Slip_SoloRoom",
        ["disputed"] = "Sparkles_Slip_Disputed",
        ["flagged"] = "Sparkles_Slip_Flagged",
        ["signin"] = "Sparkles_Slip_SignIn",
    };
    public static JObject? Build(SparklesClaimOutcome? outcome, bool gentle, Func<string, string> loc)
    {
        if (outcome == null) return null;
        var r = outcome.Receipt;
        if (r == null)
        {
            var why = outcome.Reason ?? "";
            if (why != "ceiling" && !ReasonKeys.ContainsKey(why)) return null;
            return Message(new SparklesReceipt { Reason = why }, gentle, Lines(null, why, loc), loc);
        }
        if (r.Shadow) return null;
        return Message(r, gentle, Lines(r, r.Reason, loc), loc);
    }
    // Raise-only adoption can retain local credit above the server balance.
    public static JObject? WithWallet(JObject? msg, int? local)
    {
        if (msg != null && local is int sp && msg["sp"]?.Type == JTokenType.Integer && sp > (long)msg["sp"]!)
            msg["sp"] = sp;
        return msg;
    }
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
}
