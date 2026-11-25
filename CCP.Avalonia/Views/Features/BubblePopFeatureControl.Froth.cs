using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Features;

/// <summary>Froth's options in the gold box (WPF Features/BubblePopFeatureControl.Froth.cs): biggest
/// merge, pop reach, cursor push. Clamps in Core <see cref="FrothOptions"/>.</summary>
public partial class BubblePopFeatureControl
{
    private readonly SuperOptionRows _frothRows = new(SuperEffect.InnerBloom, "FrothRows");

    private void InitFrothRows()
    {
        _frothRows.Slider("super_froth_size", FrothOptions.MaxSizeMin, FrothOptions.MaxSizeMax, FrothOptions.MaxSizeStep,
            s => s.FrothMaxSize, (s, v) => s.FrothMaxSize = FrothOptions.ClampMaxSize(v), s => s.FrothMaxSize.ToString("0.0") + "x");
        _frothRows.Slider("super_froth_reach", FrothOptions.ReachMin, FrothOptions.ReachMax, FrothOptions.ReachStep,
            s => s.FrothPopReach, (s, v) => s.FrothPopReach = FrothOptions.ClampReach((int)System.Math.Round(v)), s => Pct(s.FrothPopReach));
        _frothRows.Slider("super_froth_push", FrothOptions.PushMin, FrothOptions.PushMax, FrothOptions.PushStep,
            s => s.FrothCursorPush, (s, v) => s.FrothCursorPush = FrothOptions.ClampPush((int)System.Math.Round(v)), s => Pct(s.FrothCursorPush));
        SuperBoxMain.Body = _frothRows;
    }

    private static string Pct(int v) => v <= 0 ? Loc.Get("super_opt_off") : $"{v}%";

    private void RefreshFrothRows() => _frothRows.Refresh();
}
