namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Whether the app may ask about achievement sharing at this moment.
    ///
    /// <para>The offer used to be an ownerless <c>MessageBox</c> fired the instant a Discord link
    /// resolved. A link resolves whenever the OAuth round trip finishes, which can be minutes after
    /// the click - long enough for the user to have started a game - and an ownerless message box
    /// takes the active window as its owner. One player linked Discord, opened Down the Rabbit
    /// Hole, and got the prompt laid over the two doors with the choice underneath it unreachable
    /// (ticket 2026-09-15). Nothing about a permanent opt-in is worth interrupting a run for, so a
    /// game on screen sends it to the Inbox instead, where it waits to be asked for.</para>
    /// </summary>
    public static class AchievementSharePromptRule
    {
        /// <summary>The web host cannot see in-panel Descent; the panic surface registry can.
        /// Preserve this entry point for callers and the ticket regression tests.</summary>
        internal static bool GameOnScreen(bool webGameHostUp)
            => webGameHostUp || Safety.GameSurfaces.AnyOwnsTheScreen();

        public static AchievementSharePromptRouting Decide(bool alreadySharing, bool gameHostUp,
            bool launcherHasTheScreen, bool panelOnScreen, bool userAsked = false)
            => AchievementSharePromptDecision.Decide(alreadySharing, gameHostUp,
                launcherHasTheScreen, panelOnScreen, userAsked);
    }
}
