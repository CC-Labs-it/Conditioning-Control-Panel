using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// The pure half of T&amp;C's Scrolller pictures and its Sparkles medal (CONTRACT.md sections 1, 2, 3.1 and 5).
/// The theme ids map to subs the host owns (index checked 2026-11-08,
/// <c>handoff-1108/tc-scrolller-niches/index.json</c>); the page can only name a theme, never a sub.
/// Nothing is fetched unless the app's online source is on and consent was given (<see cref="PicturesOn"/>).
/// </summary>
internal static class TcMediaRules
{
    public const string SettingKey = "media.remoteConsent";

    /// <summary>The page's feed holds at most this many rows.</summary>
    public const int MaxRows = 36;

    /// <summary>Every theme's subs, from the checked index (pictures on the first top page).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ThemeSubs = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["trance"] = new[] { "EroticHypnosis", "HypnoGoneWild", "HypnoCaptions" },
        ["bimbo"] = new[] { "bimbofication", "Bimbos", "bimbo" },
        ["drone"] = new[] { "Dronification", "ShinyPorn", "LatexFetish" },
        ["sissy"] = new[] { "sissyhypno", "Sissies", "sissycaptions" },
        ["goon"] = new[] { "GOONED", "gooncaptions", "edging" },
        ["chastity"] = new[] { "chastity", "chastitytraining", "ChastityCouples" },
        ["censored"] = new[] { "censoredporn", "Censored_Porn", "BetaCensored" },
        ["infection"] = new[] { "BimboTransformation", "Dollification", "BimboHypno" },
    };

    /// <summary>The general hypno mix: before Pick a Theme, and for a list with no known id.</summary>
    public static readonly IReadOnlyList<string> GeneralSubs = new[] { "EroticHypnosis", "bimbofication", "Dronification", "sissyhypno" };

    /// <summary>Subs for the page's niche list: <c>[]</c> = the general mix, one theme = its subs, two
    /// themes = an even mix (interleaved). Unknown ids are dropped; nothing known left = general.</summary>
    public static IReadOnlyList<string> SubsFor(IReadOnlyList<string>? niches)
    {
        var lists = new List<string[]>();
        if (niches != null)
            foreach (var id in niches)
                if (id != null && ThemeSubs.TryGetValue(id, out var subs) && !lists.Contains(subs)) lists.Add(subs);
        if (lists.Count == 0) return GeneralSubs;
        var mix = new List<string>();
        for (int i = 0; ; i++)
        {
            bool any = false;
            foreach (var l in lists)
                if (i < l.Length) { mix.Add(l[i]); any = true; }
            if (!any) break;
        }
        return mix;
    }

    /// <summary>The Scrolller gate: the app's consent given, not offline, the player has not switched pictures off
    /// in this window, and either the app's online source is on or the player said yes in this game
    /// (<paramref name="gameYes"/> = <c>AppSettings.TcPictures</c>, the Racing Thoughts pattern,
    /// <c>GlassroadRules.PicturesOn</c>). The game's yes never changes the Library's own source.</summary>
    public static bool PicturesOn(bool? gameYes, string? mediaSource, bool consent, bool offline, bool offInWindow)
        => consent && !offline && !offInWindow
           && (gameYes == true || !string.Equals(mediaSource ?? "local", "local", StringComparison.OrdinalIgnoreCase));

    /// <summary>Why a player's YES on the picture card could not switch pictures on: <c>disabled</c> (offline mode,
    /// which no answer to the consent question can change), <c>declined</c> (no app consent), or null (on).
    /// 7.4.2: a local app source is no longer a refusal: the player's yes in the game turns the game's own
    /// pictures on (<see cref="PicturesOn"/>).</summary>
    public static string? RefusalReason(bool consent, bool offline)
    {
        if (!ConsentAskMatters(offline)) return "disabled";
        if (!consent) return "declined";
        return null;
    }

    /// <summary>Whether the app's consent question can change anything: not while Offline mode is on (the host then
    /// answers <c>disabled</c> at once and never asks). On a local app source it can: a yes is this game's choice
    /// (7.4 desk run: a fresh profile on the default local source was asked, said Yes, and was refused anyway).</summary>
    public static bool ConsentAskMatters(bool offline) => !offline;

    /// <summary>The <c>setting</c> echo for the consent key, with its reason code when it stayed off.</summary>
    public static JObject SettingEcho(bool on, string? reason)
    {
        var o = new JObject { ["type"] = "setting", ["key"] = SettingKey, ["value"] = on };
        if (!on && !string.IsNullOrEmpty(reason)) o["reason"] = reason;
        return o;
    }

    /// <summary><c>caps.scrolller</c>: device origin, files served from ccp.assets, clips allowed.</summary>
    public static JObject ScrolllerCap(bool consent)
        => new() { ["consent"] = consent, ["origin"] = "device", ["mediaOrigin"] = "https://ccp.assets", ["clips"] = true };

    /// <summary>
    /// <c>scrolller-feed</c>: up to <see cref="MaxRows"/> rows, about one clip in three when the pool has
    /// clips, the rest stills, read from <paramref name="offset"/> so a refresh deals new ones first.
    /// </summary>
    public static JObject Feed(IReadOnlyList<string> stills, IReadOnlyList<string> clips, int offset)
    {
        var rows = new JArray();
        int clipRows = Math.Min(clips.Count, stills.Count == 0 ? MaxRows : MaxRows / 3);
        int stillRows = Math.Min(stills.Count, MaxRows - clipRows);
        for (int i = 0; i < clipRows; i++)
            rows.Add(new JObject { ["url"] = clips[Wrap(offset + i, clips.Count)], ["kind"] = "video" });
        for (int i = 0; i < stillRows; i++)
            rows.Add(new JObject { ["url"] = stills[Wrap(offset + i, stills.Count)], ["kind"] = "image" });
        return new JObject { ["type"] = "scrolller-feed", ["v"] = 1, ["rows"] = rows };
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    /// <summary>The <c>fuzz-ended</c> claim detail: the ending id and minutes, nothing else. The server
    /// prices it (first ever paid T&amp;C ending gold, one a day); the host never guesses.</summary>
    public static JObject ClaimDetail(string ending, int minutes)
        => new() { ["ending"] = ending, ["minutes"] = Math.Max(0, minutes) };
}
