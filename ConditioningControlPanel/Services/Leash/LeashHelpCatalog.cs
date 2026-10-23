using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// One "?" slip on the holder side: what the other player gets when the holder presses a button.
/// <paramref name="Stamp"/> is the lowest leash level that allows it (null = any level).
/// <paramref name="YouKey"/> / <paramref name="YouText"/> / <paramref name="YouArg"/> /
/// <paramref name="YouPrefix"/> make the label of the button drawn on the YOU side.
/// <paramref name="CaptionNamesThem"/> marks a caption with a {0} for the other player's name.
/// </summary>
public sealed record LeashHelpEntry(
    string Id,
    string TitleKey,
    LeashIntensity? Stamp,
    string? YouKey,
    string? YouText,
    int? YouArg,
    string YouPrefix,
    string CaptionKey,
    string LimitsKey,
    string Pictogram,
    bool CaptionNamesThem = false);

/// <summary>
/// The holder's "?" slips (owner-approved "ink slips" mockup, 2026-10-17): one per holder button
/// (Assign, Reward, Punish, Tug), one per sheet row and one for Let go. Words only: nothing here
/// decides anything. Stamps come from <see cref="LeashGrammar.LowestIntensity"/>, so a slip can
/// never claim a level the wire would refuse. Pure.
/// </summary>
public static class LeashHelpCatalog
{
    /// <summary>The four holder buttons, in the order the card draws them.</summary>
    public static readonly IReadOnlyList<string> HolderButtons = new[] { "assign", "reward", "punish", "tug" };

    public const string LetGoId = "letgo";
    public const string StickerId = "sticker";

    /// <summary>Every pictogram the slip control can draw.</summary>
    public static readonly IReadOnlyList<string> Pictograms = new[]
    {
        "assign", "tug", "letgo", "lines", "pink", "bubbles", "detention", "video", "chaster",
        "sticker", "praise", "pardon", "credit",
    };

    private const string Min = "leash_assign_minutes_n";
    private const string Dnd = "leash_slip_dnd_lim";

    private static readonly IReadOnlyList<LeashHelpEntry> _all = Build();

    public static IReadOnlyList<LeashHelpEntry> All => _all;

    private static IReadOnlyList<LeashHelpEntry> Build()
    {
        var list = new List<LeashHelpEntry>
        {
            new("assign", "leash_btn_assign", null, Min, null, 30, "", Cap("assign"), Lim("assign"), "assign"),
            new("tug", "leash_btn_tug", null, "leash_btn_tug", null, null, "", Cap("tug"), Lim("tug"), "tug", CaptionNamesThem: true),
            new(LetGoId, "leash_slip_letgo", null, "leash_slip_letgo", null, null, "", Cap(LetGoId), Lim(LetGoId), "letgo"),
            // The Punish button: its stamp is the lowest level any punishment needs.
            new("punish", "leash_btn_punish", LowestPunish(), "leash_btn_punish", null, null, "", Cap("punish"), Lim("punish"), "lines"),
            new("reward", "leash_btn_reward", null, "leash_btn_reward", null, null, "", Cap("reward"), Dnd, "sticker"),
        };
        foreach (var k in LeashLevelGuide.Order)
            list.Add(Punish(k));
        list.Add(new(StickerId, "leash_rew_sticker", null, "leash_rew_sticker_star", null, null, "", Cap(StickerId), Dnd, "sticker"));
        list.Add(new("praise", "leash_rew_praise", null, "leash_praise_proud", null, null, "", Cap("praise"), Dnd, "praise", CaptionNamesThem: true));
        list.Add(new("pardon", "leash_rew_pardon", null, "leash_rew_pardon", null, null, "", Cap("pardon"), Lim("pardon"), "pardon"));
        list.Add(new("credit", "leash_rew_credit", null, Min, null, 30, "-", Cap("credit"), Dnd, "credit"));
        return list;
    }

    private static LeashHelpEntry Punish(PunishKind k)
    {
        var id = Id(k);
        var stamp = LeashGrammar.LowestIntensity(k);
        return k switch
        {
            PunishKind.Lines => new(id, LeashLevelGuide.NameKey(k), stamp, "leash_slip_you_lines", null, 5, "", Cap(id), Lim(id), id),
            PunishKind.Pink => new(id, LeashLevelGuide.NameKey(k), stamp, Min, null, 15, "", Cap(id), Lim(id), id),
            PunishKind.Bubbles => new(id, LeashLevelGuide.NameKey(k), stamp, null, "100", null, "", Cap(id), Lim(id), id),
            PunishKind.Detention => new(id, LeashLevelGuide.NameKey(k), stamp, Min, null, 20, "", Cap(id), Lim(id), id),
            PunishKind.Video => new(id, LeashLevelGuide.NameKey(k), stamp, "leash_pick", null, null, "", Cap(id), Lim(id), id),
            _ => new(id, LeashLevelGuide.NameKey(k), stamp, Min, null, 30, "+", Cap(id), Lim(id), id),
        };
    }

    private static string Cap(string id) => "leash_slip_" + id + "_cap";
    private static string Lim(string id) => "leash_slip_" + id + "_lim";

    public static string Id(PunishKind k) => k.ToString().ToLowerInvariant();

    public static LeashIntensity LowestPunish() => LeashLevelGuide.Order.Select(LeashGrammar.LowestIntensity).Min();

    public static LeashHelpEntry? Get(string id) => _all.FirstOrDefault(e => e.Id == id);

    public static LeashHelpEntry ForPunish(PunishKind k) => Get(Id(k))!;

    /// <summary>Every assign row (minutes, quests, video) shows the Assign slip.</summary>
    public static LeashHelpEntry ForAssign(AssignKind k) => Get("assign")!;

    public static LeashHelpEntry ForReward(RewardKind k) => k switch
    {
        RewardKind.Sticker => Get(StickerId)!,
        RewardKind.Praise => Get("praise")!,
        RewardKind.Pardon => Get("pardon")!,
        _ => Get("credit")!,
    };

    /// <summary>The label drawn in the stamp: "ANY LEVEL", "SOFT+", "STANDARD+", "STRICT".
    /// <paramref name="loc"/> resolves a loc key.</summary>
    public static string StampText(LeashIntensity? level, Func<string, string> loc, System.Globalization.CultureInfo? culture = null)
    {
        culture ??= System.Globalization.CultureInfo.CurrentUICulture;
        if (level is not LeashIntensity l) return loc("leash_slip_any");
        var name = loc(LeashLevelGuide.LevelKey(l)).ToUpper(culture);
        return l == LeashIntensity.Strict ? name : name + "+";
    }

    /// <summary>The label of the button drawn on the YOU side ("30 min", "+30 min", "Tug").</summary>
    public static string YouLabel(LeashHelpEntry e, Func<string, string> loc)
    {
        string body;
        if (e.YouKey == null) body = e.YouText ?? "";
        else if (e.YouArg is int n) body = string.Format(System.Globalization.CultureInfo.InvariantCulture, loc(e.YouKey), n);
        else body = loc(e.YouKey);
        return e.YouPrefix + body;
    }

    /// <summary>Every loc key a slip can show (the suite checks each language carries them all).</summary>
    public static IEnumerable<string> AllKeys()
    {
        yield return "leash_slip_you";
        yield return "leash_slip_them";
        yield return "leash_slip_any";
        foreach (var l in LeashLevelGuide.Levels) yield return LeashLevelGuide.LevelKey(l);
        foreach (var e in _all)
        {
            yield return e.TitleKey;
            if (e.YouKey != null) yield return e.YouKey;
            yield return e.CaptionKey;
            yield return e.LimitsKey;
        }
    }
}
