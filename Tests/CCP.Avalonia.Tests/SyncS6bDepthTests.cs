using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SyncS6bDepthTests
{
    [Theory]
    [InlineData(false, 0, 9, 166)]
    [InlineData(true, 9, 0, 89)]
    public void ContactBandsHaveFixedDepthAndTintedInk(bool left, double x, double y, byte alpha)
    {
        var brush = DepthPaint.Well(Colors.Red, 9, left);
        Assert.Equal(RelativeUnit.Absolute, brush.EndPoint.Unit);
        Assert.Equal(new Point(x, y), brush.EndPoint.Point);
        Assert.Equal(alpha, brush.GradientStops[0].Color.A);
        Assert.True(brush.GradientStops[0].Color.R > brush.GradientStops[0].Color.B);
        Assert.Equal(0, brush.GradientStops[1].Color.A);
        Assert.Equal(brush.GradientStops[0].Color.R, brush.GradientStops[1].Color.R);
    }

    [Theory]
    [InlineData("banner")]
    [InlineData("home")]
    [InlineData("hud")]
    public async Task ChromeDepthPreservesContentAndTracksTheFill(string area)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            AmbientBeatLoopsTests.Setup();
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var w = new MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                if (area == "banner")
                {
                    var banner = w.Named<Border>("HeaderBannerHost")!;
                    var glow = w.Named<Border>("HeaderBannerGlowHost")!;
                    Assert.Null(banner.Effect);
                    Assert.NotNull(glow.Effect);
                    Assert.False(glow.IsHitTestVisible);
                    var header = Assert.IsType<Grid>(banner.Parent);
                    Assert.True(header.Children.IndexOf(glow) < header.Children.IndexOf(banner));
                }
                if (area == "home")
                {
                    var home = w.Named<SettingsTabView>("SettingsTab")!;
                    foreach (var name in new[] { "LogoBrandFrame", "DashBillboard" })
                    {
                        var frame = home.FindControl<Border>(name)!;
                        var well = Assert.IsType<Grid>(frame.Child);
                        var content = well.Children.Last();
                        home.PaintDepthHome(Colors.Red);
                        home.PaintDepthHome(Colors.Blue);
                        Assert.Same(well, frame.Child);
                        Assert.Equal(4, well.Children.Count);
                        Assert.Same(content, well.Children.Last());
                        Assert.False(well.Children[0].IsHitTestVisible);
                        Assert.Equal(Color.Parse("#FF120D20"), Assert.IsType<SolidColorBrush>(frame.Background).Color);
                        var paint = Assert.IsType<LinearGradientBrush>(((Border)well.Children[0]).Background);
                        Assert.Equal(Color.FromArgb(166, 0, 0, 0), paint.GradientStops[0].Color);
                    }
                }
                if (area == "hud")
                {
                    w.PaintDepthHud(Colors.Red);
                    Assert.Equal(9, w.Named<Border>("HudDepthTop")!.Height);
                    Assert.Equal(6, w.Named<Border>("XpDepthLeft")!.Width);
                    Assert.Equal(Color.Parse("#FF120D20"), Assert.IsAssignableFrom<ISolidColorBrush>(w.Named<Border>("XPBarTrack")!.Background).Color);
                    Assert.NotNull(w.Named<Border>("XpDepthGloss")!.Background);
                    var fill = w.Named<Border>("XPBar")!;
                    var sheen = w.Named<Border>("XPBarSheen")!;
                    Assert.Contains(sheen, Assert.IsType<Panel>(fill.Child).Children);
                    foreach (var width in new[] { 4, 5, 40, 0, 12 })
                    {
                        fill.Width = width;
                        w.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        Assert.Equal(fill.Bounds.Width >= 5, w.Named<Border>("XpDepthBead")!.IsVisible);
                    }
                }
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }
}
