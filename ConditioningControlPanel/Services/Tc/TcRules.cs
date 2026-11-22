using System;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// The pure half of the Terms &amp; Conditioning desktop host (game tree <c>Resources/web/fuzz/</c>,
/// wire <c>fuzz/CONTRACT.md</c> plus Contract v2 in <c>handoff-1122/TC-EDITIONS-PLAN.md</c>): the edition
/// rule and the <c>init.caps</c> fields this host owns. No I/O and no App statics, so every rule is tested
/// without a window. The host feeds it <c>TierGate.HasPremium</c> (any subscriber, grace included).
/// </summary>
internal static class TcRules
{
    /// <summary>The page's host protocol (bridge.js PROTOCOL); a mismatch is the page's boot-error.</summary>
    public const int Protocol = 1;

    public const string EditionSmall = "small";
    public const string EditionFull = "full";
    public const string Platform = "desktop";

    /// <summary>The page never sends a save bigger than this (Contract v2 <c>save-offer</c>).</summary>
    public const int MaxSaveBytes = 512_000;

    /// <summary>Where a downloaded Expo is served from (its own virtual host, Allow, so the game on
    /// <c>ccp.game</c> can fetch it).</summary>
    public const string ExpoHost = "ccp.expo";
    public const string ExpoBase = "https://" + ExpoHost + "/";

    /// <summary>Any subscriber (tier 1 or above: Basic and Prime) plays the full game; everyone else
    /// the small one. The tile is never locked: this only picks which game it opens.</summary>
    public static string EditionFor(int tier) => tier >= 1 ? EditionFull : EditionSmall;

    /// <summary>Same rule from the app's Basic bar read (<c>TierGate.HasPremium</c>).</summary>
    public static string EditionFor(bool anySubscriber) => anySubscriber ? EditionFull : EditionSmall;

    public static bool IsFull(string? edition) => edition == EditionFull;

    /// <summary>
    /// The four Contract v2 fields of <c>init.caps</c> this host owns: <c>edition</c>, <c>platform</c>,
    /// <c>saveSlot</c> and, only for the full edition, <c>expo</c>. The v1 fields (scrolller, sparkles,
    /// words, toys) are added by the host next to these. <paramref name="saveSlot"/> is true only for a
    /// signed-in account (the slot lives on the server).
    /// </summary>
    public static JObject Caps(string edition, bool saveSlot, JObject? expoCap = null)
    {
        var e = IsFull(edition) ? EditionFull : EditionSmall;
        var caps = new JObject
        {
            ["edition"] = e,
            ["platform"] = Platform,
            ["saveSlot"] = saveSlot,
        };
        if (e == EditionFull && expoCap != null) caps["expo"] = expoCap;
        return caps;
    }

    /// <summary>Does an invite week (a free account on a friend's invite, counted by
    /// <c>HasPremiumAccess</c> but not by <c>EntitlementTierSync.EffectiveTierNow</c>) open the full game?
    /// Yes, like every Basic door (owner call owed: this one line is the switch).</summary>
    public const bool InviteWeekIsSubscriber = true;

    /// <summary>The subscriber read the edition takes: the app's Basic bar, with the invite week rule above.</summary>
    public static bool IsSubscriber(bool hasPremiumAccess, bool inviteWeekOnly)
        => hasPremiumAccess && (InviteWeekIsSubscriber || !inviteWeekOnly);

    /// <summary>Medal for a T&amp;C ending (<c>fuzz-ended</c>): every ending pays the finished fee, a
    /// first-ever ending gold. Owner call owed; the server prices it and holds the one-a-day rule.</summary>
    public const string EndingMedal = "none";
    public const string FirstEndingMedal = "gold";

    public static string MedalFor(bool firstEverEnding) => firstEverEnding ? FirstEndingMedal : EndingMedal;
}
