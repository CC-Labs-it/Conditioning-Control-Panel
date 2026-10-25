namespace ConditioningControlPanel.Services.Privacy;

// Copy of Decide/enum from ConditioningControlPanel/Services/AchievementSharePromptRule.cs (base 73b1ccf0d).
// The WPF file cannot move here unchanged (its GameOnScreen reads head-only Safety.GameSurfaces) and WPF
// stays frozen (P118), so this twin lives under its own namespace to avoid CS0436 with the head type.
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
