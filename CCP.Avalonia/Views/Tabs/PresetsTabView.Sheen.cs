using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// The selected-card sheen itself lives in PresetsTabView.axaml.cs (RefreshCardSheen). This adds the
// two refresh triggers WPF also has: the motion gate and a mod switch (re-tint).
public partial class PresetsTabView
{
    internal CardSheenAdorner? SelectedCardSheen => _cardSheen;
    internal global::Avalonia.Controls.Control? SelectedSheenHost => _cardSheen == null ? null : _cardSheenHost;

    private void InitializeCardSheen()
    {
        AttachedToVisualTree += (_, _) =>
        {
            AmbientFxCanvas.Env.MotionGateChanged += RefreshCardSheen;
            CoreMods.ModChanged += SheenModChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshCardSheen;
            CoreMods.ModChanged -= SheenModChanged;
        };
    }

    private void SheenModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() =>
    {
        // WPF TabFx_ModChanged detaches first, so Start re-reads the new glow colour.
        DetachCardSheen(); RefreshCardSheen();
    });
}
