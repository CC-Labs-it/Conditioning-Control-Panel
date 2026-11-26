using System.Linq;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 desk run (wave 4, lane D item 3): inside the Studio rack the Haptics page sat at its content's
/// desired width (about 860 px) in the middle of the pane, with an empty band between the module
/// list and the cards. It fills the pane like the other rack pages and still stops at 1280 px.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class HapticsPaneWidthTests
{
    private static StackPanel Root(HapticsTabView view) =>
        (StackPanel)RowsRender.Descendants(view).OfType<ScrollViewer>().First().Content;

    [Theory]
    [InlineData(1160)]
    [InlineData(1000)]
    public void The_page_fills_the_rack_pane(double width)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var view = new HapticsTabView();
            RowsRender.Host(view, width);
            var root = Root(view);
            // Margin 6 left + 18 right is the page's own gutter.
            Assert.InRange(root.ActualWidth, width - 24 - 2, width - 24 + 0.5);
        });
    }

    [Fact]
    public void A_very_wide_host_still_stops_at_1280()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var view = new HapticsTabView();
            RowsRender.Host(view, 1700);
            Assert.InRange(Root(view).ActualWidth, 1279.5, 1280.5);
        });
    }
}
