using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>The two rooms on the launcher's right side. The panel card on the left is not a door.</summary>
public enum LauncherDoor
{
    Games,
    Tools,
}

/// <summary>
/// What the door rules need to know about a catalogue entry, and nothing a UI type could carry.
/// <paramref name="Listed"/> false keeps the id out of every page while it stays resolvable by
/// <c>Find</c>, <c>--game</c> and old shortcuts.
/// </summary>
public readonly record struct LauncherDoorCard(string Id, LauncherDoor Door, bool Listed, bool Available, bool IsNew);

/// <summary>
/// The decisions behind the launcher's doors: which cards a door's page shows, the two numbers a
/// door card wears (how many, how many new), which game "Continue" starts, and what counts as
/// seen. Pure and static so the Avalonia head copies the rule and repaints.
/// </summary>
public static class LauncherDoorRules
{
    public const string GamesKey = "games";
    public const string ToolsKey = "tools";

    /// <summary>The door's stable key: the word the panel's seam passes ("games" / "tools").</summary>
    public static string Key(LauncherDoor door) => door == LauncherDoor.Tools ? ToolsKey : GamesKey;

    public static bool TryParse(string? key, out LauncherDoor door)
    {
        var k = key?.Trim();
        if (string.Equals(k, ToolsKey, StringComparison.OrdinalIgnoreCase)) { door = LauncherDoor.Tools; return true; }
        door = LauncherDoor.Games;
        return string.Equals(k, GamesKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The ids on a door's page, in catalogue order: that door, listed, available.</summary>
    public static IReadOnlyList<string> Page(IEnumerable<LauncherDoorCard> cards, LauncherDoor door) =>
        (cards ?? Enumerable.Empty<LauncherDoorCard>())
            .Where(c => c.Door == door && c.Listed && c.Available)
            .Select(c => c.Id)
            .ToList();

    /// <summary>The number on the door card: how many cards its page holds.</summary>
    public static int Count(IEnumerable<LauncherDoorCard> cards, LauncherDoor door) => Page(cards, door).Count;

    /// <summary>The NEW cards on a door's page the player has not been shown yet.</summary>
    public static IReadOnlyList<string> NewIds(IEnumerable<LauncherDoorCard> cards, LauncherDoor door,
                                               IEnumerable<string>? seen)
    {
        var known = new HashSet<string>(seen ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        return (cards ?? Enumerable.Empty<LauncherDoorCard>())
            .Where(c => c.Door == door && c.Listed && c.Available && c.IsNew && !known.Contains(c.Id))
            .Select(c => c.Id)
            .ToList();
    }

    /// <summary>The number on the "N new" stamp. 0 means no stamp.</summary>
    public static int NewCount(IEnumerable<LauncherDoorCard> cards, LauncherDoor door, IEnumerable<string>? seen) =>
        NewIds(cards, door, seen).Count;

    /// <summary>
    /// The seen-set after a door's page has been opened: what was seen before plus every NEW card
    /// on that page now. Order kept, no duplicates, so the stored list never churns.
    /// </summary>
    public static List<string> MarkSeen(IEnumerable<LauncherDoorCard> cards, LauncherDoor door,
                                        IEnumerable<string>? seen)
    {
        var result = new List<string>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in (seen ?? Enumerable.Empty<string>()).Concat(NewIds(cards, door, seen)))
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (known.Add(id.Trim())) result.Add(id.Trim());
        }
        return result;
    }

    /// <summary>
    /// An unlisted id answers for the listed card that took its place. The Breakout demo folded
    /// into the one Breakout card, so a demo played from an old shortcut still feeds Continue.
    /// </summary>
    public static string ListedId(string id) =>
        string.Equals(id?.Trim(), "breakoutdemo", StringComparison.OrdinalIgnoreCase) ? "breakout" : (id ?? "").Trim();

    /// <summary>
    /// The game "Continue" starts, or null for no button: the last played id, when it is still a
    /// listed, available card behind the Games door. A tool is never continued.
    /// </summary>
    public static string? ContinuePick(IEnumerable<LauncherDoorCard> cards, string? lastPlayedId)
    {
        if (string.IsNullOrWhiteSpace(lastPlayedId)) return null;
        var want = ListedId(lastPlayedId);
        foreach (var c in cards ?? Enumerable.Empty<LauncherDoorCard>())
        {
            if (!string.Equals(c.Id, want, StringComparison.OrdinalIgnoreCase)) continue;
            return c.Door == LauncherDoor.Games && c.Listed && c.Available ? c.Id : null;
        }
        return null;
    }

    /// <summary>True when a launch of this card is worth remembering for Continue.</summary>
    public static bool RemembersLaunch(LauncherDoor door) => door == LauncherDoor.Games;

    /// <summary>Only games get a desktop shortcut; a tool is a door into the panel or an editor.</summary>
    public static bool OffersShortcut(LauncherDoor door) => door == LauncherDoor.Games;
}
