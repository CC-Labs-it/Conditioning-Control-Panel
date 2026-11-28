using System;

namespace ConditioningControlPanel.Models;

/// <summary>
/// ccp-bugs #1705: a video quest says which videos count. The counter is
/// QuestService.TrackVideoMinutes, fed only by AchievementService.TrackVideoWatched, whose callers
/// are the app's own players: mandatory videos (Takeover's too), floating videos and Bubble Count
/// clips. Browser video never reaches it. This only changes the copy, never what counts.
/// </summary>
public static class QuestVideoCopy
{
    public const string ShortKey = "quest_video_counts_short";
    public const string FullKey = "quest_video_counts_full";

    /// <summary>The card line: the quest's own text plus a short "app videos only" hint.</summary>
    public static string Describe(QuestCategory category, string description, Func<string, string> loc)
    {
        if (category != QuestCategory.Video || string.IsNullOrWhiteSpace(description)) return description;
        var hint = loc(ShortKey);
        if (string.IsNullOrWhiteSpace(hint) || hint == ShortKey) return description;
        var body = description.TrimEnd();
        if (!body.EndsWith(".", StringComparison.Ordinal)) body += ".";
        return body + " " + hint;
    }

    /// <summary>The localized quest text, or the hardcoded one when the lookup came back empty or as its own key
    /// (server-rolled quests have no loc key).</summary>
    public static string LocalizedOr(string? localized, string fallback) =>
        string.IsNullOrWhiteSpace(localized) || localized.StartsWith("quest_", StringComparison.Ordinal) ? fallback : localized;

    /// <summary>The hover text naming every video that counts; null for other quests.</summary>
    public static string? Tooltip(QuestCategory category, Func<string, string> loc)
    {
        if (category != QuestCategory.Video) return null;
        var full = loc(FullKey);
        return string.IsNullOrWhiteSpace(full) || full == FullKey ? null : full;
    }
}
