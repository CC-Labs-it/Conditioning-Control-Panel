namespace ConditioningControlPanel.Views.Controls.Companion;

/// <summary>WPF CompanionWheelRelay's half-pixel edge rule, in Core so Linux CI tests it. Only the WPF head
/// calls it; Avalonia lists rely on native scroll chaining instead.</summary>
public static class CompanionWheelLogic
{
    public static bool ShouldForward(double scrollableHeight, double verticalOffset, double delta)
    {
        if (delta == 0) return false;
        if (scrollableHeight <= 0) return true;
        return delta < 0 ? verticalOffset >= scrollableHeight - 0.5 : verticalOffset <= 0.5;
    }
}
