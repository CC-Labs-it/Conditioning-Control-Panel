using CCP.Avalonia.Testing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BannerDrumTests
{
    [Fact]
    public void ShellPaintAndRollUseTheLiveBannerControls()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var w = new MainShellWindow();
            try
            {
                var host = w.Named<Border>("HeaderBannerHost")!;
                Assert.Equal(31, host.Height);
                Assert.True(w.Named<Grid>("BannerDrum")!.ClipToBounds);
                foreach (var name in new[] { "BannerDrumShade", "BannerDrumLip", "BannerDrumFoot", "BannerGlass" })
                {
                    var layer = w.Named<Border>(name)!;
                    Assert.False(layer.IsHitTestVisible);
                    Assert.Null(layer.Effect);
                }
                var hue = Color.FromRgb(40, 120, 200);
                w.PaintBannerDepth(hue);
                var shade = Assert.IsType<LinearGradientBrush>(w.Named<Border>("BannerDrumShade")!.Background);
                Assert.Equal(4, shade.GradientStops.Count);
                Assert.Equal(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellTopAlpha), shade.GradientStops[0].Color.ToUInt32());
                Assert.Equal(0, shade.GradientStops[1].Color.A);
                Assert.Equal(0, shade.GradientStops[2].Color.A);
                Assert.Equal(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellLeftAlpha), shade.GradientStops[3].Color.ToUInt32());
                var lip = Assert.IsType<LinearGradientBrush>(w.Named<Border>("BannerDrumLip")!.Background);
                Assert.Equal(RelativeUnit.Absolute, lip.EndPoint.Unit);
                Assert.Equal(10, lip.EndPoint.Point.X);
                var a = w.Named<TextBlock>("TxtBannerPrimary")!;
                var b = w.Named<TextBlock>("TxtBannerSecondary")!;
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
                w.Crossfade(a, b);
                var group = Assert.IsType<TransformGroup>(b.RenderTransform);
                Assert.Equal(13, Assert.IsType<TranslateTransform>(group.Children[1]).Y);
                CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
                w.Crossfade(a, b);
                Assert.Null(a.RenderTransform);
                Assert.Null(b.RenderTransform);
                Assert.NotEmpty(b.Transitions!);
                Assert.False(a.IsHitTestVisible);
                Assert.True(b.IsHitTestVisible);
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                w.Crossfade(b, a);
                Assert.Null(a.Transitions);
                Assert.Null(b.Transitions);
                Assert.Equal(1, a.Opacity);
                Assert.Equal(0, b.Opacity);
            }
            finally { w.Close(); }
        });
    }
}
