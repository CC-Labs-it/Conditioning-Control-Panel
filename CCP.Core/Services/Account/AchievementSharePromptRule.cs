namespace ConditioningControlPanel.Services.Privacy;

// Adapted from the WPF rule at base 73b1ccf0d793bb3fde6091c18a9fa6b5c692964a.
public enum AchievementSharePromptRouting { Skip, Ask, Inbox }

public static class AchievementSharePromptRule
{
    public static AchievementSharePromptRouting Decide(bool alreadySharing, bool gameHostUp,
        bool launcherHasTheScreen, bool panelOnScreen, bool userAsked = false)
    {
        if (alreadySharing) return AchievementSharePromptRouting.Skip;
        if (userAsked) return AchievementSharePromptRouting.Ask;
        return gameHostUp || launcherHasTheScreen || !panelOnScreen
            ? AchievementSharePromptRouting.Inbox : AchievementSharePromptRouting.Ask;
    }
}
