using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChasterGroundTests
{
    [Fact]
    public Task GroundSitsBehindThePageAndDoesNotBlockInput() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var tab = new ChasterTabView();
        var backdrop = tab.FindControl<Border>("GroundBackdrop")!;
        var glow = tab.FindControl<Border>("GroundGlow")!;
        var vignette = tab.FindControl<Border>("GroundVignette")!;
        var page = tab.FindControl<ScrollViewer>("PageScroll")!;
        var ground = Assert.IsType<Grid>(page.Parent);
        Assert.Equal(new Control[] { backdrop, glow, vignette, page }, ground.Children);
        Assert.False(backdrop.IsHitTestVisible);
        Assert.False(glow.IsHitTestVisible);
        Assert.False(vignette.IsHitTestVisible);
        var backdropBrush = Assert.IsType<RadialGradientBrush>(backdrop.Background);
        Assert.Equal(Color.Parse("#FF2C1450"), backdropBrush.GradientStops[0].Color);
        Assert.Equal(Color.Parse("#FF07040F"), backdropBrush.GradientStops[3].Color);
        Assert.Equal(0.3, glow.Opacity);
        Assert.IsType<RadialGradientBrush>(glow.Background);
        var vignetteBrush = Assert.IsType<RadialGradientBrush>(vignette.Background);
        Assert.Equal(Color.Parse("#B0000000"), vignetteBrush.GradientStops[2].Color);
        return Task.CompletedTask;
    });
}
