using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// WPF SettingsTabView.Depth.cs:16, static section-coloured Home shadows.
public partial class SettingsTabView
{
    internal void PaintDepthHome(Color hue)
    {
        var ink = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32()));
        var middle = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.ShadowAlpha * 0.58));
        var clear = Color.FromArgb(0, ink.R, ink.G, ink.B);
        Resources["HomeDepthDropBand"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(ink, 0), new(clear, 1) }
        };
        Resources["HomeDepthDropDisc"] = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(ink, 0), new(middle, 0.6), new(clear, 1) }
        };
    }
}
