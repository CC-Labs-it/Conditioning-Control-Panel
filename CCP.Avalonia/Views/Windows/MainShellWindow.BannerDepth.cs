using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.HudDepth.cs:61: the clear middle keeps the banner text readable.
    private void PaintBannerDepth(Color hue)
    {
        var top = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellTopAlpha));
        var foot = Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellLeftAlpha));
        var shade = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        shade.GradientStops.Add(new GradientStop(top, 0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, top.R, top.G, top.B), 0.34));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, foot.R, foot.G, foot.B), 0.70));
        shade.GradientStops.Add(new GradientStop(foot, 1));
        var lip = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(10, 0, RelativeUnit.Absolute) };
        lip.GradientStops.Add(new GradientStop(foot, 0));
        lip.GradientStops.Add(new GradientStop(Color.FromArgb(0, foot.R, foot.G, foot.B), 1));
        if (Named<Border>("BannerDrumShade") is { } drum) drum.Background = shade;
        if (Named<Border>("BannerDrumLip") is { } edge) edge.Background = lip;
    }
}
