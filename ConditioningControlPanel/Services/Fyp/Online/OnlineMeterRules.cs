using System;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>The meter's colour: pink, amber from <see cref="OnlineMeterRules.AmberFrom"/>, grey
/// once the day's share is used up.</summary>
internal enum OnlineMeterTone { Pink, Amber, Spent }

/// <summary>
/// Everything the "Online media today" surfaces show (title bar pill, the Assets tab card, the
/// one-time official note), decided from plain inputs so the rules are pinned by tests. The
/// copy keys live here too: one plan, one line, the same on every surface.
/// </summary>
/// <param name="Show">The meter is on screen: the media source includes online media and the
/// server switch is on.</param>
/// <param name="Percent">0..100, 100 only when spent (<see cref="ScrolllerLedger.Percent"/>).</param>
/// <param name="UnderKey">The line under the bar while some share is left.</param>
/// <param name="UpsellKey">The plan line ("Free plan. Basic gets about 7 times more..."), null on Prime.</param>
/// <param name="SeePlansTier">Tier "See plans" offers (1 = Basic, 2 = Prime), null on Prime.</param>
/// <param name="ShowOfficialNote">The one-time "Online media is official" note.</param>
/// <param name="ShowStampedLine">The stamped "OFFICIAL" credit line (replaces the plain credit).</param>
internal readonly record struct OnlineMeterState(
    bool Show,
    int Percent,
    OnlineMeterTone Tone,
    bool Spent,
    ScrolllerPlan Plan,
    string UnderKey,
    string? UpsellKey,
    int? SeePlansTier,
    bool ShowOfficialNote,
    bool ShowStampedLine);

internal static class OnlineMeterRules
{
    /// <summary>The ring and bar turn amber from this percent (owner mockup, 2026-10-19).</summary>
    public const int AmberFrom = 80;

    /// <summary>The meter refreshes at most this often on a stream of charges.</summary>
    public static readonly TimeSpan Throttle = TimeSpan.FromSeconds(1);

    /// <summary>How long the once-a-day spent notice stays in the corner (paused while hovered).</summary>
    public const double NoticeLifetimeMs = 10_000;

    /// <summary>True when the app-wide media source pulls online media ("online" or "mixed").</summary>
    public static bool IncludesOnline(string? mediaSource)
        => string.Equals(mediaSource, "online", StringComparison.Ordinal)
           || string.Equals(mediaSource, "mixed", StringComparison.Ordinal);

    public static OnlineMeterTone ToneFor(int percent, bool spent)
        => spent ? OnlineMeterTone.Spent : percent >= AmberFrom ? OnlineMeterTone.Amber : OnlineMeterTone.Pink;

    public static OnlineMeterState For(string? mediaSource, bool switchOn, int percent, bool spent,
        ScrolllerPlan plan, bool official, bool noteSeen)
    {
        var show = switchOn && IncludesOnline(mediaSource);
        var pct = spent ? 100 : Math.Clamp(percent, 0, 99);
        return new OnlineMeterState(
            Show: show,
            Percent: pct,
            Tone: ToneFor(pct, spent),
            Spent: spent,
            Plan: plan,
            UnderKey: UnderKey(plan),
            UpsellKey: UpsellKey(plan),
            SeePlansTier: SeePlansTier(plan),
            ShowOfficialNote: show && official && !noteSeen,
            ShowStampedLine: show && official);
    }

    public static string UnderKey(ScrolllerPlan plan) => plan switch
    {
        ScrolllerPlan.Prime => "online_meter_under_prime",
        ScrolllerPlan.Basic => "online_meter_under_basic",
        _ => "online_meter_under_free",
    };

    public static string? UpsellKey(ScrolllerPlan plan) => plan switch
    {
        ScrolllerPlan.Prime => null,
        ScrolllerPlan.Basic => "online_meter_upsell_basic",
        _ => "online_meter_upsell_free",
    };

    /// <summary>The tier the vault gate opens on: Free sees Basic, Basic sees Prime.</summary>
    public static int? SeePlansTier(ScrolllerPlan plan) => plan switch
    {
        ScrolllerPlan.Prime => null,
        ScrolllerPlan.Basic => 2,
        _ => 1,
    };

    public static string OfficialPlanKey(ScrolllerPlan plan) => plan switch
    {
        ScrolllerPlan.Prime => "online_official_plan_prime",
        ScrolllerPlan.Basic => "online_official_plan_basic",
        _ => "online_official_plan_free",
    };

    /// <summary>"Back at midnight, in {0}": minutes rounded up under an hour ("40 min", never
    /// "0 min"), else the nearest whole hour ("6 h" at 5 h 59). Returns the loc key and its number.</summary>
    public static (string Key, int Value) BackIn(DateTime now, DateTime reset)
    {
        var left = reset - now;
        var minutes = (int)Math.Ceiling(left.TotalMinutes);
        if (minutes < 60) return ("online_meter_minutes", Math.Max(1, minutes));
        return ("online_meter_hours", Math.Max(1, (int)Math.Round(left.TotalHours, MidpointRounding.AwayFromZero)));
    }

    /// <summary>The ring's arc as a fraction of the circle: the percent, with a sliver at 1-2%
    /// so a started day is visible; a spent day draws no arc (the grey track says it).</summary>
    public static double RingSweep(int percent, bool spent)
    {
        if (spent || percent <= 0) return 0;
        return Math.Clamp(Math.Max(percent, 3) / 100.0, 0, 0.999);
    }
}
