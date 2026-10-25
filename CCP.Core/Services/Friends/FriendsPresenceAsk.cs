using System;
using System.IO;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>The drawer's one-time presence question. The service stores the answer; this marker
/// only remembers that the player chose, including choosing to stay hidden.</summary>
public static class FriendsPresenceAsk
{
    public static bool ShouldAsk(bool available, bool shared, bool asked) => available && !shared && !asked;

    public static bool Asked(string userDataDir)
    {
        try { return File.Exists(MarkerPath(userDataDir)); } catch { return false; }
    }

    public static void MarkAsked(string userDataDir)
    {
        try { File.WriteAllText(MarkerPath(userDataDir), DateTime.UtcNow.ToString("o")); } catch { }
    }

    private static string MarkerPath(string userDataDir) => Path.Combine(userDataDir, "friends_presence_asked.flag");
}
