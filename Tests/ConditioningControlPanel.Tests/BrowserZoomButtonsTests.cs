using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Browser;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1694: the dashboard browser's toolbar carries the Deeper player's + / - page zoom
/// (10% a press, 25% to 500%), and the buttons stay on the strip in a non-maximised window.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BrowserZoomButtonsTests
{
    [Theory]
    [InlineData(0.5, +1, 0.6)]
    [InlineData(0.5, -1, 0.4)]
    [InlineData(0.75, +1, 0.85)]
    [InlineData(1.0, -1, 0.9)]
    [InlineData(0.3, -1, 0.25)]   // floor
    [InlineData(0.25, -1, 0.25)]
    [InlineData(4.95, +1, 5.0)]   // ceiling
    [InlineData(5.0, +1, 5.0)]
    [InlineData(double.NaN, +1, 1.1)]
    [InlineData(0, -1, 0.9)]
    public void One_press_moves_ten_percent_inside_the_clamp(double current, int direction, double expected)
        => Assert.Equal(expected, BrowserZoomStep.Next(current, direction));

    [Fact]
    public void Repeated_presses_land_on_whole_percents()
    {
        var z = 0.5;
        for (int i = 0; i < 7; i++) z = BrowserZoomStep.Next(z, +1);
        Assert.Equal(1.2, z);
        for (int i = 0; i < 9; i++) z = BrowserZoomStep.Next(z, -1);
        Assert.Equal(0.3, z);
    }

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    // Common non-maximised window widths minus the 96 px rail (1585 is the width the fold arrow
    // tests use). At MainWindow's MinWidth (1131) the card is about 360 px and the strip is
    // overfull on every build: the card clips its right end there, zoom in partly with it.
    [Theory]
    [InlineData(1280 - 96, 720)]
    [InlineData(1366 - 96, 720)]
    [InlineData(1440 - 96, 800)]
    [InlineData(1585 - 96, 865)]
    public void The_zoom_buttons_sit_on_the_strip_clear_of_their_neighbours(double width, double height)
        => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        var host = new Grid();
        host.Children.Add(page);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();

        var zoomOut = Bounds(page.BtnBrowserZoomOut, host);
        var zoomIn = Bounds(page.BtnBrowserZoomIn, host);
        var reload = Bounds((FrameworkElement)page.FindName("BtnReloadBrowser"), host);
        var arrow = Bounds(page.BtnFoldBrowser, host);
        var card = Bounds(page.BrowserCardFrame, host);

        Assert.Equal(Visibility.Visible, page.BrowserZoomCluster.Visibility);
        Assert.True(zoomOut.Width >= 22 && zoomIn.Width >= 22, "the zoom buttons are squashed");
        Assert.True(zoomOut.Left >= reload.Right, "zoom out overlaps the reload button");
        Assert.True(zoomIn.Left >= zoomOut.Right, "the two zoom buttons overlap");
        Assert.True(arrow.Left >= zoomIn.Right, $"the fold arrow ({arrow.Left}) covers zoom in ({zoomIn.Right})");
        Assert.True(zoomIn.Top >= card.Top && zoomIn.Bottom <= card.Bottom, "zoom in is off the card");
        Assert.True(zoomIn.Right <= card.Right - 9, $"zoom in ({zoomIn.Right}) is past the card edge ({card.Right})");
        Assert.Equal("−", page.BtnBrowserZoomOut.Content);
        Assert.Equal("+", page.BtnBrowserZoomIn.Content);
        Assert.Null(page.BtnBrowserZoomIn.Effect);
        Assert.Null(page.BtnBrowserZoomOut.Effect);
    });
}
