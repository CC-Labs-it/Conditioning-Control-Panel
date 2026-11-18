using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Dares;

// THE SHARED CLIENT TYPES for Dares (CONTRACT.md beside this file). The UI builds against these and
// IDaresService only. Wire strings live in DareGrammar; nothing here talks to the network.

/// <summary>"cant" = I bet [friend] can't [what]; "duel" = who does more.</summary>
public enum DareType { Cant, Duel }

public enum DareKind { Pics, Minutes, Video, Session, Bubbles, Dare }

public enum DareDeadline { ThreeHours, Today, Weekend, Week }

public enum DareStakeKind { Sp, Lock, Lockdown, Dare }

/// <summary>Which side of a bet: From = the sender, To = the receiver.</summary>
public enum DareSide { From, To }

public enum DareState { Writing, Sent, Running, Passed, Cancelled, Lapsed, Settled }

public enum DareDebtState { Owed, Serving, Claimed, Paid, Renounced }

/// <summary>What a bet is about. <see cref="Target"/>: a count for pics / minutes / video, the
/// difficulty id ("easy" "medium" "hard") for session, the dare number for dare, null in a duel.</summary>
public sealed record DareWhat(DareKind Kind, string? Target);

/// <summary>One side's stake. <see cref="Amount"/>: Sparkles, or minutes for Lock / Lockdown.
/// <see cref="DareNumber"/>: the sealed dare's number for Dare.</summary>
public sealed record DareStake(DareStakeKind Kind, int Amount = 0, int DareNumber = 0);

/// <summary>A sealed dare on a slip: its number and who has to do it. The words are only in Discord.</summary>
public sealed record DareSeal(int Number, DareSide By);

/// <summary>A slip as the composer builds it, before it is sent.</summary>
public sealed record DareSlip(
    DareType Type,
    DareWhat What,
    DareDeadline Deadline,
    DareStake FromStake,
    DareStake ToStake,
    bool Camera);

public sealed record DareFriend(string Id, string Name, string? AvatarUrl);

public sealed record DareDebt(DareDebtState State, bool RetryUsed, bool EndedEarly);

/// <summary>One bet as the server reports it (contract object B).</summary>
public sealed record DareBet(
    string Id,
    DareSide Me,
    DareFriend Friend,
    DareType Type,
    DareWhat What,
    DareDeadline Deadline,
    DateTimeOffset EndsAt,
    bool Camera,
    DareStake FromStake,
    DareStake ToStake,
    IReadOnlyList<DareSeal> Dares,
    DareState State,
    DareSide? Winner,
    int FromCount,
    int ToCount,
    bool? FromCameraPass,
    bool? ToCameraPass,
    DareDebt? Debt,
    DateTimeOffset At,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? SettledAt,
    DareSide? RenouncedBy)
{
    /// <summary>The side that lost, when settled with a winner.</summary>
    public DareSide? Loser => Winner == null ? null : Winner == DareSide.From ? DareSide.To : DareSide.From;

    public bool IOwe => State == DareState.Settled && Loser == Me && Debt is { State: DareDebtState.Owed or DareDebtState.Serving };

    public bool TheyOwe => State == DareState.Settled && Loser != null && Loser != Me && Debt is { State: DareDebtState.Owed or DareDebtState.Serving or DareDebtState.Claimed };

    /// <summary>An offer waiting on me.</summary>
    public bool IsOfferToMe => State == DareState.Sent && Me == DareSide.To;

    /// <summary>What I would owe (my stake) and what they would owe.</summary>
    public DareStake MyStake => Me == DareSide.From ? FromStake : ToStake;
    public DareStake TheirStake => Me == DareSide.From ? ToStake : FromStake;
    public int MyCount => Me == DareSide.From ? FromCount : ToCount;
    public int TheirCount => Me == DareSide.From ? ToCount : FromCount;
}

public sealed record DaresMe(DateTimeOffset? BannedUntil, bool LockdownOk, bool Discord, int SpWonToday);

/// <summary>Everything the UI draws. <see cref="Available"/> false = the server is dark (flag off,
/// old server, signed out): the UI hides every Dares door.</summary>
public sealed record DaresSnapshot(bool Available, DaresMe Me, IReadOnlyList<DareBet> Bets)
{
    public static readonly DaresSnapshot Dark = new(false, new DaresMe(null, false, false, 0), Array.Empty<DareBet>());
}

/// <summary>What a create came back with. <see cref="WriteCode"/> is set with
/// <see cref="DareCreateStatus.Writing"/>: the sender types <c>/bet write code:&lt;code&gt;</c> in Discord.</summary>
public enum DareCreateStatus { Sent, Writing, NotFriends, Banned, FriendBanned, Limit, DiscordNeeded, LockdownNotOk, Refused, Off, TryLater, LockdownNeedsSub }

public sealed record DareCreateOutcome(DareCreateStatus Status, string? Id = null, string? WriteCode = null);

/// <summary>The presses a bet card can make. Each op does its side effects in the service
/// (Pay on a Lock debt books the minutes on the player's own Chaster tab first; StartLockdown runs
/// the real Lockdown after the UI's consent prompts and reports served / ended early).</summary>
public enum DareOp { Accept, Pass, Cancel, Pay, StartLockdown, DidIt, Tick, Renounce }

public enum DareActResult { Done, Gone, Banned, Limit, LockdownBusy, NeedsChaster, Refused, Off, TryLater, LockdownNeedsSub }
