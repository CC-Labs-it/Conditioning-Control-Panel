using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// What a launcher tile IS, with no way to start it: the id, its face and its account rule. Every
/// head builds its tiles from <see cref="LauncherCards.All"/> and attaches its own launch, so the
/// order, the art and the names are written once.
/// </summary>
/// <param name="Id">Stable id: the <c>--game &lt;id&gt;</c> and shortcut argument. Never rename.</param>
/// <param name="ArtPath">Resource-relative art, mod-resolvable. Null paints the hue plate with <paramref name="Glyph"/>.</param>
/// <param name="Rgb">Tile hue as 0xRRGGBB.</param>
/// <param name="RequiresAccount">False only for entries that explicitly allow signed-out play.</param>
/// <param name="IsNew">Shows the NEW badge.</param>
/// <param name="Door">Which door's page the card sits on (<see cref="LauncherDoorRules"/>).</param>
/// <param name="Listed">False keeps the card off every page; <c>Find</c>, <c>--game</c> and a
/// shortcut still reach it.</param>
public sealed record LauncherCard(string Id, string? ArtPath, string Glyph, uint Rgb,
    bool RequiresAccount = true, bool IsNew = false, LauncherDoor Door = LauncherDoor.Games, bool Listed = true)
{
    private string KeyPrefix => Door == LauncherDoor.Tools ? "launcher_tool_" : "launcher_game_";
    public string TitleKey => KeyPrefix + Id + "_title";
    public string BlurbKey => KeyPrefix + Id + "_blurb";

    /// <summary>The card as the door rules see it, with this head's answers for today.</summary>
    public LauncherDoorCard DoorCard(bool available, bool locked) => new(Id, Door, Listed, available, IsNew, locked);
    public byte R => (byte)(Rgb >> 16);
    public byte G => (byte)(Rgb >> 8);
    public byte B => (byte)Rgb;
}

/// <summary>
/// The launcher's tiles in launcher order: the newest, loudest room first, the quiet ones last.
/// Remote, Companion and sessions are panel features and have no tile (Sep 18 2026 owner
/// decision: the split is games vs CCP). The Graded Intake is the one panel tab with a tile.
/// </summary>
public static class LauncherCards
{
    public static readonly IReadOnlyList<LauncherCard> All = new LauncherCard[]
    {
        new("backroom", "features/backroom.png", "♦", 0xB95CD8),
        // Breakout is ONE card (declutter 2026-11-15): the full game for whoever holds it, the free
        // demo for everyone else, so it never asks for an account. The demo keeps its id for --game
        // and old shortcuts, off every page.
        new("breakoutdemo", null, "●", 0x8CF5C8, RequiresAccount: false, IsNew: true, Listed: false),
        new("breakout", null, "●", 0x55B7FF, RequiresAccount: false, IsNew: true),
        // Piece by Piece: free for everyone (owner, 2026-09-27); its lobby asks a signed-out player.
        new("piecebypiece", "features/piecebypiece.png", "♟", 0x7B5CFF, RequiresAccount: false, IsNew: true),
        new("race", "features/race.png", "☕", 0xFFB36B),
        // Racing Thoughts Classic: the 2D race back beside the 3D one, free for everyone (WPF 7.2.3, owner 2026-11-05).
        new("raceclassic", "features/race_classic.png", "☕", 0xFF8FB8),
        new("dtrh", "features/dtrh.png", "▼", 0x8CF5C8),
        new("arcademy", "features/arcademy.png", "★", 0xFF69B4),
        new("goon", "features/goon_game_tile.png", "●", 0x76C893),
        new("intake", "features/lab_quiz_hero.png", "❓", 0x8E7CF2),
    };

    /// <summary>The Tools door: editors and makers. No desktop shortcut, never a --game target;
    /// each head attaches its own open (a panel tab, the Mod Manager, a window of its own).</summary>
    public static readonly IReadOnlyList<LauncherCard> Tools = new LauncherCard[]
    {
        new("deepereditor", "features/deeper_editor.png", "✎", 0x7B5CFF, RequiresAccount: false, Door: LauncherDoor.Tools),
        new("sessioneditor", null, "▤", 0xFF69B4, RequiresAccount: false, Door: LauncherDoor.Tools),
        new("modcreator", null, "✂", 0xFFB36B, RequiresAccount: false, Door: LauncherDoor.Tools),
        new("justdrop", "features/justdrop.png", "▼", 0x8CF5C8, RequiresAccount: false, Door: LauncherDoor.Tools),
        new("loom", "features/loom.png", "◎", 0x55B7FF, RequiresAccount: false, Door: LauncherDoor.Tools),
    };

    /// <summary>Every card behind either door, games first.</summary>
    public static IEnumerable<LauncherCard> Every => All.Concat(Tools);

    /// <summary>A game card by id. Games only: the boot, --game and shortcuts never start a tool.</summary>
    public static LauncherCard? Find(string? id) => Find(All, id);

    /// <summary>A card behind either door by id.</summary>
    public static LauncherCard? FindAny(string? id) => Find(Every, id);

    private static LauncherCard? Find(IEnumerable<LauncherCard> cards, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = id.Trim();
        return cards.FirstOrDefault(c => string.Equals(c.Id, key, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The launcher's close button: Veto under Lockdown (never a way around the veil); Hide while
/// something runs or the panel is up (the panel/tray is the way back); Exit when nothing is left.</summary>
public enum LauncherCloseOutcome { Veto, Hide, Exit }

/// <summary>A tile's Play, in check order: SignIn (account needed, nobody signed in), Refuse (tier-locked:
/// the host's toast, launcher stays up), Leash (the panel comes up with the gate), Launch.</summary>
public enum LauncherGameStep { SignIn, Refuse, Leash, Launch }

/// <summary>Launch-handoff and panel-to-tray rules; each head's host only acts on the answer.</summary>
public static class LauncherRules
{
    /// <summary>The longest the window may hold its exit beat before it hides, in ms.</summary>
    public const int MaxHideDelayMs = 1500;

    /// <summary>How long the launcher takes to fade to nothing before it hides, in ms.</summary>
    public const int FadeOutMs = 220;

    public static int ClampHideDelay(int ms) => Math.Clamp(ms, 0, MaxHideDelayMs);

    /// <summary>The fade is the tail of an exit beat, never added after it: with a 450 ms beat
    /// and a 220 ms fade, the fade starts at 230. A beat shorter than the fade fades at once.</summary>
    public static int FadeLeadMs(int delayMs) => Math.Max(0, ClampHideDelay(delayMs) - FadeOutMs);

    /// <param name="panelExists">False when there is no panel to exit through; then only hide.</param>
    public static LauncherCloseOutcome Close(bool lockdown, bool somethingRunning, bool panelOnScreen, bool panelExists)
    {
        if (lockdown) return LauncherCloseOutcome.Veto;
        if (somethingRunning || panelOnScreen || !panelExists) return LauncherCloseOutcome.Hide;
        return LauncherCloseOutcome.Exit;
    }

    public static LauncherGameStep Game(bool needsAccount, bool locked, bool leashBlocks)
    {
        if (needsAccount) return LauncherGameStep.SignIn;
        if (locked) return LauncherGameStep.Refuse;
        if (leashBlocks) return LauncherGameStep.Leash;
        return LauncherGameStep.Launch;
    }
}
