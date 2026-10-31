using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private bool _hudDepthWired;

    internal void PaintDepthHud(Color hue)
    {
        if (Named<Border>("HudDepthTop") is { } band)
            band.Background = DepthPaint.Well(hue, DepthLaw.WellPx);
        if (Named<Border>("XpDepthTop") is { } top)
            top.Background = DepthPaint.Well(hue, 5);
        if (Named<Border>("XpDepthLeft") is { } left)
            left.Background = DepthPaint.Well(hue, 6, left: true);
        if (Named<Border>("XPBar") is { } fill && Named<Border>("XpDepthBead") is { } bead)
        {
            bead.IsVisible = fill.Bounds.Width >= 5;
            if (!_hudDepthWired)
            {
                _hudDepthWired = true;
                fill.SizeChanged += (_, e) => bead.IsVisible = e.NewSize.Width >= 5;
            }
        }
    }
}
