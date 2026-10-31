using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Reflection;
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
    [InlineData("logo")]
    [InlineData("billboard")]
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
                if (area == "logo")
                {
                    var home = w.Named<SettingsTabView>("SettingsTab")!;
                    var frame = home.FindControl<Border>("LogoBrandFrame")!;
                    var flip = home.FindControl<Grid>("LogoFlipHost")!;
                    var face = home.FindControl<Border>("LogoFaceLogo")!;
                    var pass = home.FindControl<Border>("IntakePassFace")!;
                    Assert.Same(flip, frame.Child);
                    Assert.Same(flip, face.Parent);
                    Assert.Equal(new Thickness(24), face.Margin);
                    Assert.Equal(new CornerRadius(12), face.CornerRadius);
                    Assert.True(face.ClipToBounds);
                    Assert.Equal(new Thickness(1), face.BorderThickness);
                    Assert.Equal(Color.Parse("#FF120D20"), Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
                    Assert.True(Application.Current!.TryFindResource("DepthPressedBevel", out var bevel));
                    Assert.Same(bevel, face.BorderBrush);
                    var well = Assert.IsType<Grid>(face.Child);
                    var top = home.FindControl<Border>("LogoWellTop")!;
                    var left = home.FindControl<Border>("LogoWellLeft")!;
                    Assert.Equal(14, top.Height);
                    Assert.Equal(9, left.Width);
                    Assert.False(top.IsHitTestVisible);
                    Assert.False(left.IsHitTestVisible);
                    Assert.True(well.Children.IndexOf(top) < well.Children.IndexOf(home.FindControl<Control>("ImgLogo")!));
                    Assert.Same(well, home.FindControl<Control>("LogoSheen")!.Parent);
                    var drift = Assert.IsType<ScaleTransform>(face.RenderTransform);
                    drift.ScaleX = 1.04;
                    Assert.True(face.IsVisualAncestorOf(top));
                    Assert.True(flip.IsVisualAncestorOf(top));
                    var switchFace = typeof(MainShellWindow).GetMethod("SetIntakePassFace", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    foreach (var showPass in new[] { false, true, false })
                    {
                        switchFace.Invoke(w, new object[] { showPass });
                        Assert.Equal(!showPass, top.IsEffectivelyVisible);
                        Assert.Equal(showPass, pass.IsEffectivelyVisible);
                        Assert.Same(well, face.Child);
                        Assert.Equal(1.04, drift.ScaleX);
                    }
                }
                if (area == "billboard")
                {
                    var home = w.Named<SettingsTabView>("SettingsTab")!;
                    var frame = home.FindControl<Border>("DashBillboard")!;
                    var slot = home.FindControl<Grid>("BillboardHostSlot")!;
                    var well = Assert.IsType<Grid>(frame.Child);
                    var bands = well.Children.OfType<Border>().ToArray();
                    var left = Assert.Single(bands, b => b.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Left);
                    Assert.Equal(6, left.Width);
                    Assert.Equal(9, Assert.Single(bands, b => b.VerticalAlignment == global::Avalonia.Layout.VerticalAlignment.Top).Height);
                    Assert.Equal(1, Assert.Single(bands, b => b.VerticalAlignment == global::Avalonia.Layout.VerticalAlignment.Bottom).Height);
                    Assert.Same(slot, well.Children[0]);
                    Assert.Equal(3, bands.Length);
                    Assert.All(bands, b => { Assert.False(b.IsHitTestVisible); Assert.NotNull(b.Background); });
                    Assert.Equal(Color.Parse("#FF120D20"), Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color);
                    Assert.True(Application.Current!.TryFindResource("DepthPressedBevel", out var bevel));
                    Assert.Same(bevel, frame.BorderBrush);
                    w.ShowTab("presets");
                    w.ShowTab("settings");
                    Assert.Same(well, frame.Child);
                    Assert.Same(slot, well.Children[0]);
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
