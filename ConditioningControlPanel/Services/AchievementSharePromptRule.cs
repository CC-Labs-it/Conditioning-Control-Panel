namespace ConditioningControlPanel.Services
{
    /// <summary>WPF's screen probe and compatibility entry point. The routing decision is shared
    /// with the Avalonia head in Core; game surfaces themselves belong to the head.</summary>
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
