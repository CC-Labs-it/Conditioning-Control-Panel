using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    // The well paint is behind the contents. Neither logo animation nor billboard layout owns it.
    internal void PaintDepthHome(Color hue)
    {
        PaintWell(this.FindControl<Border>("LogoBrandFrame"), hue, 14);
        PaintWell(this.FindControl<Border>("DashBillboard"), hue, DepthLaw.WellPx);
    }

    private static void PaintWell(Border? frame, Color hue, double depth)
    {
        if (frame == null) return;
        frame.Background = new SolidColorBrush(Color.Parse("#FF120D20"));
        if (frame.Child is not Panel { Tag: "depth-well" } well)
        {
            var content = frame.Child;
            frame.Child = null;
            well = new Grid { Tag = "depth-well" };
            well.Children.Add(new Border { Height = depth, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top, IsHitTestVisible = false });
            well.Children.Add(new Border { Width = DepthLaw.WellPx, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, IsHitTestVisible = false });
            well.Children.Add(new Border { Height = 1, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), IsHitTestVisible = false });
            if (content != null) well.Children.Add(content);
            frame.Child = well;
        }
        ((Border)well.Children[0]).Background = DepthPaint.Well(hue, depth, tinted: false);
        ((Border)well.Children[1]).Background = DepthPaint.Well(hue, DepthLaw.WellPx, left: true, tinted: false);
    }
}
