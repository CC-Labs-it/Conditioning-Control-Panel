namespace ConditioningControlPanel.Services;

/// <summary>WPF dashboard tiles swap open/toggle, while page links keep their ordinary click.</summary>
public static class DashboardClickRule
{
    public static bool ShouldToggle(bool right, bool invert, bool dashboard, bool toggleable = true)
        => right != (invert && dashboard && toggleable);
}
