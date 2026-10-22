namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF Services/Friends/FriendsSfx.cs: the drawer's cues, same files, levels and tags, played
/// through <see cref="FriendsLanding.Sfx"/> (master volume, the incoming gap, half level in a session).</summary>
internal static class FriendsSfx
{
    public static void Click() => FriendsLanding.Sfx("chaos/ui_click.mp3", 0.12f, "friends-click");
    public static void Accepted() => FriendsLanding.Sfx("chaos/ui_unlock.mp3", 0.18f, "friends-accepted");
    public static void Dismiss() => FriendsLanding.Sfx("chaos/sink.mp3", 0.10f, "friends-sink");
    public static void Denied() => FriendsLanding.Sfx("chaos/ui_denied.mp3", 0.14f, "friends-denied");
}
