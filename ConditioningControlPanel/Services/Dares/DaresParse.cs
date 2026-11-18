using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// The wire's JSON into the shared model and back (CONTRACT.md "Ops", object B). Tolerant on purpose:
/// a bet that does not fit the grammar is dropped, never thrown on. Pure.
/// </summary>
public static class DaresParse
{
    // ---- out ----

    /// <summary>The <c>slip</c> of a <c>create</c>: type, what, deadline, stakes, camera. Never any text.</summary>
    public static JObject SlipToWire(DareSlip slip)
    {
        var what = new JObject { ["kind"] = DareGrammar.Wire(slip.What.Kind) };
        if (!string.IsNullOrEmpty(slip.What.Target))
        {
            if (slip.What.Kind == DareKind.Session) what["target"] = slip.What.Target;
            else if (int.TryParse(slip.What.Target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) what["target"] = n;
        }
        return new JObject
        {
            ["type"] = DareGrammar.Wire(slip.Type),
            ["what"] = what,
            ["deadline"] = DareGrammar.Wire(slip.Deadline),
            ["stakes"] = new JObject { ["from"] = StakeToWire(slip.FromStake), ["to"] = StakeToWire(slip.ToStake) },
            ["camera"] = slip.Camera && DareGrammar.CameraAllowed(slip.What.Kind),
        };
    }

    public static JObject StakeToWire(DareStake s)
    {
        var o = new JObject { ["kind"] = DareGrammar.Wire(s.Kind) };
        if (s.Kind == DareStakeKind.Dare) o["n"] = s.DareNumber;
        else o["amount"] = s.Amount;
        return o;
    }

    // ---- in ----

    /// <summary>Reads a <c>state</c> reply into a snapshot. Null when the reply is not an ok one.</summary>
    public static DaresSnapshot? State(JObject? reply)
    {
        if (reply == null || reply.Value<bool?>("ok") != true) return null;
        var me = Me(reply["me"] as JObject);
        var bets = new List<DareBet>();
        if (reply["bets"] is JArray arr)
            foreach (var t in arr)
                if (Bet(t as JObject) is { } b) bets.Add(b);
        return new DaresSnapshot(true, me, bets);
    }

    public static DaresMe Me(JObject? o)
    {
        if (o == null) return new DaresMe(null, false, false, 0);
        return new DaresMe(
            TimeOrNull(o["banned_until"]),
            o.Value<bool?>("lockdown_ok") == true,
            o.Value<bool?>("discord") == true,
            IntOr(o["sp_won_today"], 0));
    }

    /// <summary>One B, or null when it does not fit the grammar.</summary>
    public static DareBet? Bet(JObject? o)
    {
        if (o == null) return null;
        var id = Str(o["id"]);
        var role = DareGrammar.ParseSide(Str(o["role"]));
        var type = DareGrammar.ParseType(Str(o["type"]));
        var state = DareGrammar.ParseState(Str(o["state"]));
        var deadline = DareGrammar.ParseDeadline(Str(o["deadline"]));
        var whatO = o["what"] as JObject;
        var kind = DareGrammar.ParseKind(Str(whatO?["kind"]));
        if (string.IsNullOrEmpty(id) || role == null || type == null || state == null || deadline == null || kind == null) return null;

        var target = whatO?["target"] is JValue tv && tv.Type != JTokenType.Null
            ? Convert.ToString(tv.Value, CultureInfo.InvariantCulture)
            : null;
        var friendO = o["friend"] as JObject;
        var friend = new DareFriend(Str(friendO?["id"]) ?? "", Str(friendO?["name"]) ?? "", Str(friendO?["avatar"]));
        var stakesO = o["stakes"] as JObject;
        var from = Stake(stakesO?["from"] as JObject);
        var to = Stake(stakesO?["to"] as JObject);
        if (from == null || to == null) return null;

        var seals = new List<DareSeal>();
        if (o["dares"] is JArray da)
            foreach (var d in da)
                if (d is JObject dj && IntOr(dj["n"], 0) is var n && n > 0 && DareGrammar.ParseSide(Str(dj["by"])) is { } by)
                    seals.Add(new DareSeal(n, by));

        var counts = o["counts"] as JObject;
        var pass = o["camera_pass"] as JObject;
        DareDebt? debt = null;
        if (o["debt"] is JObject dbt && DareGrammar.ParseDebtState(Str(dbt["state"])) is { } ds)
            debt = new DareDebt(ds, dbt.Value<bool?>("retry_used") == true, dbt.Value<bool?>("ended_early") == true);

        var at = TimeOrNull(o["at"]) ?? DateTimeOffset.MinValue;
        return new DareBet(
            id!, role.Value, friend, type.Value, new DareWhat(kind.Value, target), deadline.Value,
            TimeOrNull(o["ends_at"]) ?? at, o.Value<bool?>("camera") == true, from, to, seals, state.Value,
            DareGrammar.ParseSide(Str(o["winner"])),
            IntOr(counts?["from"], 0), IntOr(counts?["to"], 0),
            BoolOrNull(pass?["from"]), BoolOrNull(pass?["to"]),
            debt, at, TimeOrNull(o["accepted_at"]), TimeOrNull(o["settled_at"]),
            DareGrammar.ParseSide(Str(o["renounced_by"])));
    }

    public static DareStake? Stake(JObject? o)
    {
        var kind = DareGrammar.ParseStakeKind(Str(o?["kind"]));
        if (kind == null) return null;
        return kind == DareStakeKind.Dare
            ? new DareStake(kind.Value, 0, IntOr(o!["n"], 0))
            : new DareStake(kind.Value, IntOr(o!["amount"], 0));
    }

    // ---- statuses ----

    /// <summary>A <c>create</c> reply as the UI reads it.</summary>
    public static DareCreateOutcome Create(JObject? reply)
    {
        if (reply == null) return new DareCreateOutcome(DareCreateStatus.TryLater);
        if (reply.Value<bool?>("ok") != true) return new DareCreateOutcome(Refusal(reply) switch
        {
            DareActResult.Off => DareCreateStatus.Off,
            DareActResult.TryLater => DareCreateStatus.TryLater,
            _ => DareCreateStatus.Refused,
        });
        var status = Str(reply["status"]) switch
        {
            "sent" => DareCreateStatus.Sent,
            "writing" => DareCreateStatus.Writing,
            "not_friends" => DareCreateStatus.NotFriends,
            "banned" => DareCreateStatus.Banned,
            "friend_banned" => DareCreateStatus.FriendBanned,
            "limit" => DareCreateStatus.Limit,
            "discord_needed" => DareCreateStatus.DiscordNeeded,
            "lockdown_needs_sub" => DareCreateStatus.LockdownNeedsSub,
            _ => DareCreateStatus.Refused,
        };
        return new DareCreateOutcome(status, Str(reply["id"]), Str(reply["write_code"]));
    }

    /// <summary>An op reply's <c>status</c> (accept, pay, start, served). A reply with no status that
    /// says ok is Done.</summary>
    public static DareActResult Act(JObject? reply)
    {
        if (reply == null) return DareActResult.TryLater;
        if (reply.Value<bool?>("ok") != true) return Refusal(reply);
        return Str(reply["status"]) switch
        {
            null or "running" or "paid" or "serving" or "owed" or "claimed" => DareActResult.Done,
            "gone" => DareActResult.Gone,
            "banned" => DareActResult.Banned,
            "limit" => DareActResult.Limit,
            "lockdown_busy" => DareActResult.LockdownBusy,
            "lockdown_needs_sub" => DareActResult.LockdownNeedsSub,
            _ => DareActResult.Refused,
        };
    }

    /// <summary>An <c>{ ok:false, reason }</c>: off, too fast and faults read as their own kinds.</summary>
    public static DareActResult Refusal(JObject reply) => Str(reply["reason"]) switch
    {
        "off" => DareActResult.Off,
        "too_fast" or "busy" => DareActResult.TryLater,
        "gone" or "not_found" => DareActResult.Gone,
        "banned" => DareActResult.Banned,
        _ => DareActResult.Refused,
    };

    public static bool IsOff(JObject? reply) =>
        reply != null && reply.Value<bool?>("ok") == false && Str(reply["reason"]) == "off";

    // ---- helpers ----

    internal static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

    internal static int IntOr(JToken? t, int fallback) =>
        t is JValue { Type: JTokenType.Integer or JTokenType.Float } v ? (int)Math.Round(v.Value<double>()) : fallback;

    private static bool? BoolOrNull(JToken? t) => t?.Type == JTokenType.Boolean ? t.Value<bool>() : null;

    internal static DateTimeOffset? TimeOrNull(JToken? t)
    {
        var s = Str(t);
        return s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v) ? v : null;
    }
}
