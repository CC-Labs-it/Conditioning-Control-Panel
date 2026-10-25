namespace ConditioningControlPanel.Services.UI;

/// <summary>WPF dashboard tile gestures: only actual Home feature switches swap buttons.
/// Teasers, navigation-only cards, and cards outside Home keep their normal navigation click.</summary>
public static class DashboardClickRules
{
    public const int HoverCloseMilliseconds = 450;

    public static bool ShouldClose(bool pinned, bool anchorHovered, bool bodyHovered, bool focusWithin)
        => !pinned && !anchorHovered && !bodyHovered && !focusWithin;

    public static bool IsToggle(bool rightButton, bool dashboard, bool toggleable, bool inverted)
        => rightButton != (dashboard && toggleable && inverted);
}
