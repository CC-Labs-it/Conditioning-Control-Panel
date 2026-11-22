using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services.Friends;

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

    /// <summary>Every loc key the folds draw, for the ten-languages check.</summary>
    public static IReadOnlyList<string> AllKeys { get; } = new[]
    {
        "friends_fold_open", "friends_fold_close",
        "friends_feed_clear",
    };
}
