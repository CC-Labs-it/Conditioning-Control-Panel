namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF Services/Friends/FriendsSfx.cs: the drawer's cues, same files, levels and tags, played
/// through <see cref="FriendsLanding.Sfx"/> (master volume, the cue gaps, half level in a session).</summary>
internal static class FriendsSfx
{
    public static void Click() => FriendsLanding.Sfx("chaos/ui_click.mp3", 0.12f, "friends-click");
    public static void Sent() => FriendsLanding.Sfx("chaos/chip_pop.mp3", 0.16f, "friends-sent");
    public static void Denied() => FriendsLanding.Sfx("chaos/ui_denied.mp3", 0.14f, "friends-denied");
}
