using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>One punishment in the level guide: its name key, the line that says what it is, and the
/// figures that line is filled with (minutes for time, counts otherwise).</summary>
public sealed record LeashGuideRow(PunishKind Kind, string NameKey, string DetailKey, IReadOnlyList<int> Figures);

/// <summary>
/// What Soft, Standard and Strict allow, said from the grammar itself (CONTRACT "Grammar" and
/// "Rules") so the words next to the level picker cannot drift from what the wire accepts.
/// Explains only: nothing here decides anything. Pure.
/// </summary>
public static class LeashLevelGuide
{
    /// <summary>CONTRACT "Rules": punishments a leashed account takes in one (UTC) day.</summary>
    public const int PunishmentsPerDay = 3;

    /// <summary>CONTRACT "Rules": an unfinished punishment is dropped this long after it was sent.</summary>
    public const int ExpiresAfterHours = 72;

    public static readonly IReadOnlyList<LeashIntensity> Levels = new[] { LeashIntensity.Soft, LeashIntensity.Standard, LeashIntensity.Strict };

    /// <summary>Every punishment kind, in the order the holder's sheet lists them.</summary>
    public static readonly IReadOnlyList<PunishKind> Order = new[]
    {
        PunishKind.Lines, PunishKind.Pink, PunishKind.Bubbles, PunishKind.Detention, PunishKind.Video, PunishKind.Chaster,
    };

    /// <summary>What this level adds on top of the one before it.</summary>
    public static IReadOnlyList<PunishKind> Adds(LeashIntensity level) =>
        Order.Where(k => LeashGrammar.LowestIntensity(k) == level).ToList();

    /// <summary>Everything this level allows (its own additions and every lower level's).</summary>
    public static IReadOnlyList<PunishKind> Allows(LeashIntensity level) =>
        Order.Where(k => LeashGrammar.Allowed(k, level)).ToList();

    public static string LevelKey(LeashIntensity level) => "leash_level_" + level.ToString().ToLowerInvariant();

    /// <summary>The level in a few words ("A light touch. Good first leash.").</summary>
    public static string TaglineKey(LeashIntensity level) => "leash_guide_" + level.ToString().ToLowerInvariant();

    public static string NameKey(PunishKind kind) => "leash_pun_" + kind.ToString().ToLowerInvariant();

    /// <summary>The figures a punishment's line names. Chaster sizes are seconds on the wire and
    /// minutes here; a video has no presets, so its line carries the longest cap the leashed side
    /// can set.</summary>
    public static IReadOnlyList<int> Figures(PunishKind kind) => kind switch
    {
        PunishKind.Video => new[] { LeashVideoCap.Max },
        PunishKind.Chaster => LeashGrammar.PunishSizes(kind).Select(s => s / 60).ToList(),
        _ => LeashGrammar.PunishSizes(kind),
    };

    public static LeashGuideRow Row(PunishKind kind) =>
        new(kind, NameKey(kind), "leash_guide_" + kind.ToString().ToLowerInvariant(), Figures(kind));

    /// <summary>The table: for each level, the rows it adds.</summary>
    public static IReadOnlyList<LeashGuideRow> Rows(LeashIntensity level) => Adds(level).Select(Row).ToList();

    /// <summary>"Lines, Pink session" / "+ Chaster time": the always-visible line under a level's
    /// name. <paramref name="loc"/> resolves a loc key.</summary>
    public static string Summary(LeashIntensity level, Func<string, string> loc)
    {
        var names = string.Join(", ", Adds(level).Select(k => loc(NameKey(k))));
        return level == LeashIntensity.Soft ? names : "+ " + names;
    }

    /// <summary>A row's line with its figures filled in.</summary>
    public static string Detail(LeashGuideRow row, Func<string, string> loc) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, loc(row.DetailKey), row.Figures.Cast<object>().ToArray());

    /// <summary>The limits line, as (key, per day, hours).</summary>
    public const string LimitsKey = "leash_guide_limits";

    /// <summary>What holds at every level, as loc keys, in reading order.</summary>
    public static readonly IReadOnlyList<string> AlwaysKeys = new[]
    {
        "leash_guide_rule_level", "leash_guide_rule_dnd", "leash_guide_rule_rewards", "leash_guide_rule_cut",
    };

    /// <summary>Every loc key the guide can show (the suite checks each language carries them all).</summary>
    public static IEnumerable<string> AllKeys()
    {
        yield return "leash_guide_toggle";
        yield return "leash_guide_keeps";
        yield return "leash_guide_always";
        yield return LimitsKey;
        foreach (var l in Levels) { yield return LevelKey(l); yield return TaglineKey(l); }
        foreach (var k in Order) { yield return NameKey(k); yield return Row(k).DetailKey; }
        foreach (var k in AlwaysKeys) yield return k;
    }
}
