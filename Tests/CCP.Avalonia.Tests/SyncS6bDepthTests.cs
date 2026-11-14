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

    [Fact]
    public async Task BeadTracksFillOnLaunchBeforeAnyNavigation()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            AmbientBeatLoopsTests.Setup();
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.PlayerXP = 0;
            var w = new MainShellWindow();
            try
            {
                w.Show();
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                Assert.Equal("settings", w.CurrentTab);
                var fill = w.Named<Border>("XPBar")!;
                var bead = w.Named<Control>("XPTubeBead")!;
                // No ShowTab or PaintDepthHud: size changes must work on the first screen.
                foreach (var width in new[] { 0, 4, 5, 40, 4, 0, 8 })
                {
                    fill.Width = width;
                    w.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(width, fill.Bounds.Width);
                    Assert.Equal(width >= 5, bead.IsVisible);
                }
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("banner")]
    [InlineData("logo")]
    [InlineData("billboard")]
    [InlineData("hud")]
    [InlineData("xpCaps")]
    [InlineData("xpLayers")]
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
                    Assert.Null(glow.Effect);   // restack b222: the glow is a BoxShadow so the flash and breath never animate an Effect
                    Assert.Equal(1, glow.BoxShadow.Count); Assert.Equal(MainShellWindow.BannerGlowBlur, glow.BoxShadow[0].Blur);
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
                if (area == "xpCaps")
                {
                    var track = w.Named<Border>("XPBarTrack")!;
                    Assert.Equal(10, track.Height);   // WPF MainWindow.xaml:1864
                    Assert.Equal(new CornerRadius(5), track.CornerRadius);
                    var top = w.Named<Border>("XpDepthTop")!;
                    var left = w.Named<Border>("XpDepthLeft")!;
                    var groove = Assert.IsType<Grid>(track.Child);
                    var foot = Assert.Single(groove.Children.OfType<Border>(), b => b.Height == 1);
                    Assert.Equal(new CornerRadius(5, 5, 0, 0), top.CornerRadius);
                    Assert.Equal(new CornerRadius(5, 0, 0, 5), left.CornerRadius);
                    Assert.Equal(new Thickness(5, 0), foot.Margin);
                    Assert.Same(groove, top.Parent);
                    Assert.Same(groove, left.Parent);
                    foreach (var band in new[] { top, left, foot })
                    {
                        Assert.False(band.IsHitTestVisible);
                        Assert.True(groove.Children.IndexOf(band) < groove.Children.IndexOf(w.Named<Border>("XPBar")!));
                    }
                }
                if (area == "xpLayers")
                {
                    var fill = w.Named<Border>("XPBar")!;
                    var layers = Assert.IsAssignableFrom<Panel>(fill.Child);
                    var gloss = w.Named<Border>("XpDepthGloss")!;
                    var bead = w.Named<Control>("XPTubeBead")!;
                    var sheen = w.Named<Border>("XPBarSheen")!;
                    Assert.Same(layers, gloss.Parent);
                    Assert.Same(layers, bead.Parent);
                    Assert.True(layers.Children.IndexOf(gloss) < layers.Children.IndexOf(bead));
                    Assert.True(layers.Children.IndexOf(bead) < layers.Children.IndexOf(sheen));
                    Assert.Equal(new Thickness(4, 0), sheen.Margin);
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
                    Assert.Contains(sheen, Assert.IsAssignableFrom<Panel>(fill.Child).Children);
                    foreach (var width in new[] { 4, 5, 40, 0, 12 })
                    {
                        fill.Width = width;
                        w.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        Assert.Equal(fill.Bounds.Width >= 5, w.Named<Control>("XPTubeBead")!.IsVisible);
                    }
                }
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }
}
