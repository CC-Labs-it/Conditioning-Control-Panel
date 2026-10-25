using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// The selected-card sheen and all its triggers (tab visibility, window focus/minimise, motion gate,
// mod re-tint) live in PresetsTabView.Fx.cs. These are the test seams GateFxAdornerTests reads.
public partial class PresetsTabView
{
    internal CardSheenAdorner? SelectedCardSheen => _selectedSheen;
    internal global::Avalonia.Controls.Control? SelectedSheenHost => CardSheenHost;
}
