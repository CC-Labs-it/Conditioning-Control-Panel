namespace ConditioningControlPanel.Services;

/// <summary>Where the separate Discord achievement-sharing opt-in may be offered.</summary>
public enum AchievementSharePromptRouting { Skip, Ask, Inbox }

/// <summary>Shared by the WPF and Avalonia heads; a completed OAuth link is never consent.</summary>
public static class AchievementSharePromptDecision
{
    public static AchievementSharePromptRouting Decide(bool alreadySharing, bool gameHostUp,
        bool launcherHasTheScreen, bool panelOnScreen, bool userAsked = false)
    {
        if (alreadySharing) return AchievementSharePromptRouting.Skip;
        if (userAsked) return AchievementSharePromptRouting.Ask;
        return gameHostUp || launcherHasTheScreen || !panelOnScreen
            ? AchievementSharePromptRouting.Inbox
            : AchievementSharePromptRouting.Ask;
    }
}
