using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BannerDepthShellTests
{
    [Fact]
    public async Task SectionHuePaintsNonInteractiveDrumAndFixedLip()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var w = new MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("presets");
                var shade = w.Named<Border>("BannerDrumShade")!;
                var lip = w.Named<Border>("BannerDrumLip")!;
                var grid = Assert.IsType<Grid>(shade.Parent);
                Assert.True(grid.Children.IndexOf(shade) < grid.Children.IndexOf(w.Named<TextBlock>("TxtBannerPrimary")!));
                Assert.False(shade.IsHitTestVisible);
                Assert.False(lip.IsHitTestVisible);
                var first = Assert.IsType<LinearGradientBrush>(shade.Background);
                var firstLip = Assert.IsType<LinearGradientBrush>(lip.Background);
                Assert.Equal(RelativeUnit.Absolute, firstLip.EndPoint.Unit);
                Assert.Equal(10, firstLip.EndPoint.Point.X);
                Assert.Equal(new[] { 0.0, 0.34, 0.70, 1.0 }, first.GradientStops.Select(x => x.Offset));
                Assert.Equal(0, first.GradientStops[1].Color.A);
                Assert.Equal(0, first.GradientStops[2].Color.A);
                w.ShowTab("play");
                var hue = ConditioningControlPanel.Avalonia.Views.Controls.NavStripRules.Accent(NavSections.SectionForTab("play"));
                var second = Assert.IsType<LinearGradientBrush>(shade.Background);
                var secondLip = Assert.IsType<LinearGradientBrush>(lip.Background);
                Assert.NotEqual(first.GradientStops[0].Color, second.GradientStops[0].Color);
                Assert.NotEqual(firstLip.GradientStops[0].Color, secondLip.GradientStops[0].Color);
                Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellTopAlpha)), second.GradientStops[0].Color);
                Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellLeftAlpha)), secondLip.GradientStops[0].Color);
                Assert.NotNull(w.Named<Control>("BannerWebLink"));
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
                w.OnMotionChrome();
                Assert.Same(second, shade.Background);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }
}
