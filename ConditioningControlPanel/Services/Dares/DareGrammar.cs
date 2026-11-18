using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// THE OPTION TABLES (CONTRACT.md "Grammar"), mirrored by the server's dares.js. The composer offers
/// only what is here; the wire strings are here and nowhere else.
/// </summary>
public static class DareGrammar
{
    public static readonly IReadOnlyList<string> PicsTargets = new[] { "100", "250", "500", "1000" };
    public static readonly IReadOnlyList<string> MinutesTargets = new[] { "30", "60", "120" };
    public static readonly IReadOnlyList<string> VideoTargets = new[] { "15", "30", "60" };
    public static readonly IReadOnlyList<string> SessionTargets = new[] { "easy", "medium", "hard" };

    public static readonly IReadOnlyList<int> SpAmounts = new[] { 50, 100, 200, 500 };
    public static readonly IReadOnlyList<int> LockMinutes = new[] { 10, 20, 30 };
    public static readonly IReadOnlyList<int> LockdownMinutes = new[] { 10, 20, 30 };

    public const int CameraSampleMs = 500;
    public const double CameraPassFraction = 0.8;
    public const int MaxOpenBets = 5;
    public const int SpWinCapPerDay = 500;
    public static readonly TimeSpan RenounceBan = TimeSpan.FromDays(7);

    /// <summary>Kinds a bet type can be about, in the composer's order.</summary>
    public static IReadOnlyList<DareKind> KindsFor(DareType type) => type == DareType.Cant
        ? new[] { DareKind.Pics, DareKind.Minutes, DareKind.Video, DareKind.Session, DareKind.Dare }
        : new[] { DareKind.Pics, DareKind.Minutes, DareKind.Video, DareKind.Bubbles };

    /// <summary>Targets for a "can't" kind; empty for a duel and for Dare (its number is assigned).</summary>
    public static IReadOnlyList<string> TargetsFor(DareKind kind) => kind switch
    {
        DareKind.Pics => PicsTargets,
        DareKind.Minutes => MinutesTargets,
        DareKind.Video => VideoTargets,
        DareKind.Session => SessionTargets,
        _ => Array.Empty<string>(),
    };

    public static bool CameraAllowed(DareKind kind) => kind is DareKind.Pics or DareKind.Minutes or DareKind.Video or DareKind.Session;

    public static IReadOnlyList<int> AmountsFor(DareStakeKind kind) => kind switch
    {
        DareStakeKind.Sp => SpAmounts,
        DareStakeKind.Lock => LockMinutes,
        DareStakeKind.Lockdown => LockdownMinutes,
        _ => Array.Empty<int>(),
    };

    // ---- wire strings ----

    public static string Wire(DareType t) => t == DareType.Cant ? "cant" : "duel";
    public static string Wire(DareKind k) => k switch
    {
        DareKind.Pics => "pics", DareKind.Minutes => "minutes", DareKind.Video => "video",
        DareKind.Session => "session", DareKind.Bubbles => "bubbles", _ => "dare",
    };
    public static string Wire(DareDeadline d) => d switch
    {
        DareDeadline.ThreeHours => "3h", DareDeadline.Today => "today", DareDeadline.Weekend => "weekend", _ => "week",
    };
    public static string Wire(DareStakeKind k) => k switch
    {
        DareStakeKind.Sp => "sp", DareStakeKind.Lock => "lock", DareStakeKind.Lockdown => "lockdown", _ => "dare",
    };
    public static string Wire(DareSide s) => s == DareSide.From ? "from" : "to";

    public static DareType? ParseType(string? s) => s switch { "cant" => DareType.Cant, "duel" => DareType.Duel, _ => null };
    public static DareKind? ParseKind(string? s) => s switch
    {
        "pics" => DareKind.Pics, "minutes" => DareKind.Minutes, "video" => DareKind.Video,
        "session" => DareKind.Session, "bubbles" => DareKind.Bubbles, "dare" => DareKind.Dare, _ => null,
    };
    public static DareDeadline? ParseDeadline(string? s) => s switch
    {
        "3h" => DareDeadline.ThreeHours, "today" => DareDeadline.Today, "weekend" => DareDeadline.Weekend, "week" => DareDeadline.Week, _ => null,
    };
    public static DareStakeKind? ParseStakeKind(string? s) => s switch
    {
        "sp" => DareStakeKind.Sp, "lock" => DareStakeKind.Lock, "lockdown" => DareStakeKind.Lockdown, "dare" => DareStakeKind.Dare, _ => null,
    };
    public static DareSide? ParseSide(string? s) => s switch { "from" => DareSide.From, "to" => DareSide.To, _ => null };
    public static DareState? ParseState(string? s) => s switch
    {
        "writing" => DareState.Writing, "sent" => DareState.Sent, "running" => DareState.Running, "passed" => DareState.Passed,
        "cancelled" => DareState.Cancelled, "lapsed" => DareState.Lapsed, "settled" => DareState.Settled, _ => null,
    };
    public static DareDebtState? ParseDebtState(string? s) => s switch
    {
        "owed" => DareDebtState.Owed, "serving" => DareDebtState.Serving, "claimed" => DareDebtState.Claimed,
        "paid" => DareDebtState.Paid, "renounced" => DareDebtState.Renounced, _ => null,
    };
}
