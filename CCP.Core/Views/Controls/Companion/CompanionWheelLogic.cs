namespace ConditioningControlPanel.Views.Controls.Companion;

/// <summary>WPF's half-pixel edge tolerance, shared by both heads' bounded-list relays.</summary>
public static class CompanionWheelLogic
{
    public static bool ShouldForward(double scrollableHeight, double verticalOffset, double delta)
    {
        if (delta == 0) return false;
        if (scrollableHeight <= 0) return true;
        return delta < 0 ? verticalOffset >= scrollableHeight - 0.5 : verticalOffset <= 0.5;
    }
}
