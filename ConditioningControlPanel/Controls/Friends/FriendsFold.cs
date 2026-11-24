using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>What the drawer remembers about its folds: which of the three busy sections (leash,
/// "what happened", dares) is open, when the feed preview was last cleared, which settled dares
/// were dismissed, and which leash items the player has already looked at.</summary>
public sealed class FriendsFoldState
{
    public bool LeashOpen { get; set; }
    public bool FeedOpen { get; set; }
    public bool DaresOpen { get; set; }

    /// <summary>The feed preview shows nothing at or before this time (the header's clear).</summary>
    public DateTime? FeedClearedUtc { get; set; }

    public List<string> DaresDismissed { get; set; } = new();

    /// <summary>Leash items already seen open. Null = never filled: the first look takes what is
    /// there as seen, so an old leash does not arrive with a full badge.</summary>
    public List<string>? LeashSeen { get; set; }
}

/// <summary>
/// The drawer's folds (owner 2026-11-22, "too much clutter"). Folded, the leash is one header line
/// that says who you hold or who holds you, with a bubble for new progress; "what happened" shows
/// at most <see cref="FeedPreview"/> recent lines; dares show the one row on top. Opening a section
/// shows the whole list, newest first. The pure rules live here so the suite reads them bare; the
/// state is one small file per machine, shared by every drawer (rail chip, launcher, page).
/// </summary>
internal static class FriendsFold
{
    /// <summary>Folded, "what happened" shows at most this many lines.</summary>
    public const int FeedPreview = 2;

    /// <summary>A read line leaves the folded preview once it is this old.</summary>
    public static readonly TimeSpan FeedFresh = TimeSpan.FromHours(24);

    /// <summary>Remembered ids are capped; the oldest go first.</summary>
    public const int KeepMax = 300;

    internal static Func<FriendsFoldState> Load { get; set; } = LoadFile;
    internal static Action<FriendsFoldState> Save { get; set; } = SaveFile;

    private static FriendsFoldState? _state;

    public static FriendsFoldState State
    {
        get
        {
            if (_state != null) return _state;
            try { _state = Load() ?? new FriendsFoldState(); }
            catch { _state = new FriendsFoldState(); }
            return _state;
        }
    }

    /// <summary>Changes the state, trims it and saves it. Never throws. Every drawer reads the state
    /// fresh when it draws, so a second drawer picks the change up on its next open.</summary>
    public static void Update(Action<FriendsFoldState> change)
    {
        var s = State;
        change(s);
        Trim(s.DaresDismissed);
        if (s.LeashSeen != null) Trim(s.LeashSeen);
        try { Save(s); } catch (Exception ex) { App.Logger?.Debug("[Friends] fold save failed: {E}", ex.Message); }
    }

    /// <summary>For the suite: a fresh in-memory state, nothing on disk.</summary>
    internal static void UseMemory()
    {
        _state = new FriendsFoldState();
        OffersFoldedOver.Clear();
        Load = () => new FriendsFoldState();
        Save = _ => { };
    }

    private static void Trim(List<string> list)
    {
        if (list.Count > KeepMax) list.RemoveRange(0, list.Count - KeepMax);
    }

    private static string PathOf => Path.Combine(App.UserDataPath, "friends_folds.json");

    private static FriendsFoldState LoadFile()
    {
        var p = PathOf;
        if (!File.Exists(p)) return new FriendsFoldState();
        return JsonSerializer.Deserialize<FriendsFoldState>(File.ReadAllText(p)) ?? new FriendsFoldState();
    }

    private static void SaveFile(FriendsFoldState s) => File.WriteAllText(PathOf, JsonSerializer.Serialize(s));

    // ---- what happened --------------------------------------------------------------------

    /// <summary>The folded preview: newest first, at most <see cref="FeedPreview"/>, only lines
    /// after the last clear that are new or younger than <see cref="FeedFresh"/>.</summary>
    public static IReadOnlyList<FeedEntry> FeedPreviewLines(IReadOnlyList<FeedEntry> lines, Func<FeedEntry, bool> isNew,
        DateTime nowUtc, DateTime? clearedUtc)
        => lines.Where(l => (clearedUtc == null || l.Event.AtUtc > clearedUtc.Value)
                            && (isNew(l) || nowUtc - l.Event.AtUtc < FeedFresh))
            .Take(FeedPreview)
            .ToList();

    // ---- dares ----------------------------------------------------------------------------

    /// <summary>A settled bet with nothing left to do can be dismissed; an open one cannot.</summary>
    public static bool CanDismiss(DareBet b) => DareCardRules.RowState(b) is not (DareRowState.Offer or DareRowState.IOwe
        or DareRowState.TheyOwe or DareRowState.Running or DareRowState.Writing or DareRowState.Waiting);

    /// <summary>Folded: the one row on top of the strip (the most pressing open bet, else the newest settled one).</summary>
    public static DareBet? DareFolded(DaresSnapshot s, ICollection<string> dismissed)
        => DareCardRules.StripOrder(s).FirstOrDefault(b => !dismissed.Contains(b.Id));

    /// <summary>Open: every bet not dismissed, newest activity first.</summary>
    public static IReadOnlyList<DareBet> DaresChrono(DaresSnapshot s, ICollection<string> dismissed)
        => !s.Available ? Array.Empty<DareBet>()
            : s.Bets.Where(b => !dismissed.Contains(b.Id))
                .OrderByDescending(b => b.SettledAt ?? b.AcceptedAt ?? b.At)
                .ToList();

    // ---- leash ----------------------------------------------------------------------------

    /// <summary>Everything on the leash a player would want to know moved: an offer, a punishment
    /// or task given to me, a task of someone I hold that got done or missed, and how far each
    /// thing I sent got (seen, done, refused...). One key per item and step.</summary>
    public static IReadOnlyList<string> LeashKeys(LeashSnapshot s, Func<string, IReadOnlyList<LeashSentItem>> sentTo)
    {
        var keys = new List<string>();
        foreach (var o in s.Offers) keys.Add(OfferKey(o));
        if (s.Me is { } me)
        {
            foreach (var p in me.Pending) keys.Add("p:" + p.Pid);
            if (me.Assignment is { } a) keys.Add("a:" + a.Aid);
        }
        foreach (var h in s.Holding)
        {
            if (h.Assignment is { Status: not AssignStatus.Open } ha) keys.Add("ha:" + ha.Aid + ":" + ha.Status);
            IReadOnlyList<LeashSentItem> sent;
            try { sent = sentTo(h.Who.Id) ?? Array.Empty<LeashSentItem>(); }
            catch { sent = Array.Empty<LeashSentItem>(); }
            foreach (var i in sent)
                if (LeashSteps.Rank(i.Step) >= 2) keys.Add("r:" + i.Key + ":" + i.Step);
        }
        return keys;
    }

    private static string OfferKey(LeashOffer o) => "o:" + (o.Id ?? o.From.Id + "@" + o.At.ToUnixTimeSeconds());

    /// <summary>Incoming leash offers still inside their life (invites live 5 minutes).</summary>
    public static IReadOnlyList<string> LiveOfferKeys(LeashSnapshot s, DateTimeOffset nowUtc)
        => s.Offers.Where(o => o.ExpiresAt > nowUtc).Select(OfferKey).Distinct().ToList();

    /// <summary>Offers the player folded the leash over while they showed: they no longer open it.
    /// Memory only, never saved, so the stored fold switch stays the player's own.</summary>
    internal static HashSet<string> OffersFoldedOver { get; } = new();

    /// <summary>Whether the leash shows open: the player's stored switch, or a live incoming offer the
    /// player has not folded over (it opens the fold by itself without touching the stored switch).</summary>
    public static bool LeashShownOpen(bool storedOpen, IReadOnlyList<string> liveOffers, ICollection<string> foldedOver)
        => storedOpen || liveOffers.Any(k => !foldedOver.Contains(k));

    /// <summary>The bubble on the folded leash: keys not seen yet. Zero before the first fill.</summary>
    public static int LeashUnseen(IReadOnlyList<string> keys, ICollection<string>? seen)
        => seen == null ? 0 : keys.Distinct().Count(k => !seen.Contains(k));

    /// <summary>The folded line: who holds me, who I hold. Loc keys with one argument each.</summary>
    public static IReadOnlyList<(string Key, string Arg)> LeashSummary(LeashSnapshot s)
    {
        var parts = new List<(string, string)>();
        if (s.Me is { } me) parts.Add(("friends_leash_held_by", me.Holder.Name));
        if (s.Holding.Count > 0)
        {
            var names = s.Holding.Select(h => h.Who.Name).ToList();
            var shown = string.Join(", ", names.Take(2));
            if (names.Count > 2) shown += " +" + (names.Count - 2);
            parts.Add(("friends_leash_holding", shown));
        }
        return parts;
    }

    /// <summary>Every loc key the folds draw, for the ten-languages check.</summary>
    public static IReadOnlyList<string> AllKeys { get; } = new[]
    {
        "friends_leash_section", "friends_leash_held_by", "friends_leash_holding", "friends_fold_open", "friends_fold_close",
        "friends_feed_clear", "dares_dismiss",
    };
}
