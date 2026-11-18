using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Dares;

namespace ConditioningControlPanel.Controls.Dares;

/// <summary>How a bet reads in the Dares strip, one word for where it stands.</summary>
public enum DareRowState { Writing, Waiting, Offer, Running, IOwe, TheyOwe, Won, Lost, Draw, Paid, Renounced, Passed, Lapsed, Cancelled }

/// <summary>
/// Everything the bet cards decide that is not drawing (Tests/Dares/DareCardUiTests): the debt card's
/// button words and the op behind each, the Lockdown consent steps, the worded results, what the
/// strip shows and in what order, the bars and the time left.
/// </summary>
public static class DareCardRules
{
    /// <summary>Settled bets the strip keeps under the open ones.</summary>
    public const int SettledShown = 4;

    // ---- the debt card ----------------------------------------------------------------

    /// <summary>The Pay button's words by stake (owner's words, DARES-HANDOFF "the debt card").</summary>
    public static string PayKey(DareStakeKind k) => k switch
    {
        DareStakeKind.Sp => "dares_pay_sp",
        DareStakeKind.Lock => "dares_pay_lock",
        DareStakeKind.Dare => "dares_pay_dare",
        _ => "dares_pay_lockdown",
    };

    public static DareOp PayOp(DareStakeKind k) => k switch
    {
        DareStakeKind.Dare => DareOp.DidIt,
        DareStakeKind.Lockdown => DareOp.StartLockdown,
        _ => DareOp.Pay,
    };

    /// <summary>The prompts a press of Pay walks through before anything starts. A Lockdown always
    /// asks once (length, no panic key, the exit phrase, ending early); a PC that starts CCP with
    /// Windows gets one more. Every other stake asks nothing.</summary>
    public static IReadOnlyList<string> ConsentSteps(DareStakeKind k, bool runsAtStartup)
    {
        if (k != DareStakeKind.Lockdown) return Array.Empty<string>();
        return runsAtStartup ? new[] { "lockdown", "startup" } : new[] { "lockdown" };
    }

    /// <summary>What the card says once Pay went through.</summary>
    public static string PaidKey(DareStakeKind k) => k == DareStakeKind.Dare ? "dares_debt_claimed" : "dares_debt_done";

    /// <summary>The one debt the launch card is about: owed (not already being served), oldest first.
    /// Null when nothing is owed. The card never starts anything; the player picks.</summary>
    public static DareBet? DebtToShow(DaresSnapshot s)
    {
        if (!s.Available) return null;
        return s.Bets.Where(b => b.IOwe && b.Debt?.State == DareDebtState.Owed)
            .OrderBy(b => b.SettledAt ?? b.At).FirstOrDefault();
    }

    /// <summary>Offers waiting on me, oldest first (the Inbox posts each once).</summary>
    public static IReadOnlyList<DareBet> Offers(DaresSnapshot s)
        => !s.Available ? Array.Empty<DareBet>() : s.Bets.Where(b => b.IsOfferToMe).OrderBy(b => b.At).ToList();

    /// <summary>What the landing does with a fresh snapshot: offers to post (each once a launch),
    /// posted offers that are no longer waiting (their rows go), and the debt to put on the ladder
    /// (once a launch, never while a session or a Lockdown holds the player).</summary>
    public static (IReadOnlyList<DareBet> Post, IReadOnlyList<string> Gone, DareBet? Debt) LandingPlan(
        DaresSnapshot s, IReadOnlyCollection<string> posted, IReadOnlyCollection<string> debtShown, bool holding)
    {
        var offers = Offers(s);
        var post = offers.Where(b => !posted.Contains(b.Id)).ToList();
        var live = new HashSet<string>(offers.Select(b => b.Id));
        var gone = posted.Where(id => !live.Contains(id)).ToList();
        var debt = holding ? null : DebtToShow(s);
        if (debt != null && debtShown.Contains(debt.Id)) debt = null;
        return (post, gone, debt);
    }

    /// <summary>A Lockdown I would owe if I lose: the offer card states the consent at accept time.</summary>
    public static bool OfferNeedsConsent(DareBet b) => b.MyStake.Kind == DareStakeKind.Lockdown;

    // ---- worded results ---------------------------------------------------------------

    public static string CreateKey(DareCreateStatus s) => s switch
    {
        DareCreateStatus.Sent => "dares_create_sent",
        DareCreateStatus.Writing => "dares_create_writing",
        DareCreateStatus.NotFriends => "dares_create_not_friends",
        DareCreateStatus.Banned => "dares_create_banned",
        DareCreateStatus.FriendBanned => "dares_create_friend_banned",
        DareCreateStatus.Limit => "dares_create_limit",
        DareCreateStatus.DiscordNeeded => "dares_shut_discord",
        DareCreateStatus.LockdownNotOk or DareCreateStatus.LockdownNeedsSub => "dares_create_lockdown_needs_sub",
        DareCreateStatus.Refused => "dares_refused",
        DareCreateStatus.Off => "dares_off",
        _ => "dares_try_later",
    };

    public static bool IsGood(DareCreateStatus s) => s is DareCreateStatus.Sent or DareCreateStatus.Writing;

    public static string ActKey(DareActResult r) => r switch
    {
        DareActResult.Gone => "dares_act_gone",
        DareActResult.Banned => "dares_create_banned",
        DareActResult.Limit => "dares_create_limit",
        DareActResult.LockdownBusy => "dares_act_lockdown_busy",
        DareActResult.NeedsChaster => "dares_act_needs_chaster",
        DareActResult.LockdownNeedsSub => "dares_act_lockdown_needs_sub",
        DareActResult.Refused => "dares_refused",
        DareActResult.Off => "dares_off",
        _ => "dares_try_later",
    };

    /// <summary>The line a sender copies into Discord to write the slip's dares.</summary>
    public static string WriteLine(string code) => "/bet write code:" + code;

    // ---- the strip --------------------------------------------------------------------

    public static DareRowState RowState(DareBet b)
    {
        switch (b.State)
        {
            case DareState.Writing: return DareRowState.Writing;
            case DareState.Sent: return b.Me == DareSide.To ? DareRowState.Offer : DareRowState.Waiting;
            case DareState.Running: return DareRowState.Running;
            case DareState.Passed: return DareRowState.Passed;
            case DareState.Lapsed: return DareRowState.Lapsed;
            case DareState.Cancelled: return DareRowState.Cancelled;
        }
        if (b.Winner == null) return DareRowState.Draw;
        if (b.IOwe) return DareRowState.IOwe;
        if (b.TheyOwe) return DareRowState.TheyOwe;
        if (b.Debt?.State == DareDebtState.Renounced) return DareRowState.Renounced;
        if (b.Debt?.State == DareDebtState.Paid) return DareRowState.Paid;
        return b.Winner == b.Me ? DareRowState.Won : DareRowState.Lost;
    }

    public static string RowStateKey(DareRowState s) => "dares_row_" + s switch
    {
        DareRowState.Writing => "writing",
        DareRowState.Waiting => "waiting",
        DareRowState.Offer => "offer",
        DareRowState.Running => "running",
        DareRowState.IOwe => "i_owe",
        DareRowState.TheyOwe => "they_owe",
        DareRowState.Won => "won",
        DareRowState.Lost => "lost",
        DareRowState.Draw => "draw",
        DareRowState.Paid => "paid",
        DareRowState.Renounced => "renounced",
        DareRowState.Passed => "passed",
        DareRowState.Lapsed => "lapsed",
        _ => "cancelled",
    };

    /// <summary>Open bets first (offers to answer, my debts, what they owe, running, sent), then the
    /// newest few settled ones.</summary>
    public static IReadOnlyList<DareBet> StripOrder(DaresSnapshot s)
    {
        if (!s.Available) return Array.Empty<DareBet>();
        static int Rank(DareBet b) => RowState(b) switch
        {
            DareRowState.Offer => 0,
            DareRowState.IOwe => 1,
            DareRowState.TheyOwe => 2,
            DareRowState.Running => 3,
            DareRowState.Writing or DareRowState.Waiting => 4,
            _ => 9,
        };
        var open = s.Bets.Where(b => Rank(b) < 9).OrderBy(Rank).ThenBy(b => b.EndsAt);
        var done = s.Bets.Where(b => Rank(b) == 9).OrderByDescending(b => b.SettledAt ?? b.At).Take(SettledShown);
        return open.Concat(done).ToList();
    }

    /// <summary>May I start a new dare right now (the friends menu's Dare line)?</summary>
    public static bool CanDare(DaresSnapshot s, DateTimeOffset now)
        => s.Available && !(s.Me.BannedUntil is DateTimeOffset until && until > now);

    /// <summary>The words for how long a running bet has left, as a key and its arguments.</summary>
    public static (string Key, object[] Args) Left(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return ("dares_left_ending", Array.Empty<object>());
        if (left.TotalDays >= 1) return ("dares_left_days", new object[] { (int)left.TotalDays, left.Hours });
        if (left.TotalHours >= 1) return ("dares_left_hours", new object[] { (int)left.TotalHours, left.Minutes });
        return ("dares_left_minutes", new object[] { Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) });
    }

    /// <summary>In a "can't" only the receiver counts: the one bar shows their count against the target.</summary>
    public static int CantCount(DareBet b) => b.Me == DareSide.To ? b.MyCount : b.TheirCount;

    /// <summary>How full the "can't" bar is: count over target (a session or a dare is 0 or 1).</summary>
    public static double CantFraction(DareBet b)
    {
        var n = CantCount(b);
        if (b.What.Kind is DareKind.Session or DareKind.Dare) return n >= 1 ? 1 : 0;
        if (!int.TryParse(b.What.Target, out var target) || target <= 0) return 0;
        return Math.Clamp(n / (double)target, 0, 1);
    }

    /// <summary>The duel bars: both counts against the larger, so the leader's bar is full.</summary>
    public static (double Me, double Them) DuelBars(int me, int them)
    {
        var top = Math.Max(1, Math.Max(me, them));
        return (Math.Clamp(me / (double)top, 0, 1), Math.Clamp(them / (double)top, 0, 1));
    }

    /// <summary>The "can't" kinds that count to a number (a bar with digits) rather than a yes/no.</summary>
    public static bool CountsUp(DareKind k) => k is DareKind.Pics or DareKind.Minutes or DareKind.Video or DareKind.Bubbles;

    /// <summary>A running "can't dare" waits on the receiver's own "I did it".</summary>
    public static bool ShowsDidIt(DareBet b)
        => b.State == DareState.Running && b.Type == DareType.Cant && b.What.Kind == DareKind.Dare && b.Me == DareSide.To && b.MyCount < 1;

    /// <summary>The winner's Tick: they say they did the dare they owed.</summary>
    public static bool ShowsTick(DareBet b) => b.TheyOwe && b.Debt?.State == DareDebtState.Claimed;

    /// <summary>The desk's "settle now", preview only, on a running bet.</summary>
    public static bool ShowsSettleNow(DareBet b, bool preview) => preview && b.State == DareState.Running;

    /// <summary>A sender may take a slip back while it waits.</summary>
    public static bool ShowsCancel(DareBet b) => b.Me == DareSide.From && b.State is DareState.Writing or DareState.Sent;
}
