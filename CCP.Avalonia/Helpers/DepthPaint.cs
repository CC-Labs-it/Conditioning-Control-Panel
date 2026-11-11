using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Helpers;

// Absolute mapping keeps the contact band the same depth at every window size.
internal static class DepthPaint
{
    internal static LinearGradientBrush Well(Color hue, double depth, bool left = false)
    {
        var ink = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), left ? DepthLaw.WellLeftAlpha : DepthLaw.WellTopAlpha));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(left ? depth : 0, left ? 0 : depth, RelativeUnit.Absolute),
            GradientStops = new GradientStops
            {
                new GradientStop(ink, 0),
                new GradientStop(Color.FromArgb(0, ink.R, ink.G, ink.B), 1),
            },
        };
    }
}
