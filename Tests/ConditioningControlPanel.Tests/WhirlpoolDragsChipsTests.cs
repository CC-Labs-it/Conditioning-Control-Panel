using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 desk run (wave 4, lane D item 4): the Whirlpool "Drags in" switches sat in a WrapPanel, and at
/// the default window size "Words" wrapped alone onto a second line under Fog, Pictures and Bubbles.
/// They are two tidy rows of two at every width, each label whole.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class WhirlpoolDragsChipsTests
{
    [Theory]
    [InlineData(1160)]
    [InlineData(900)]
    [InlineData(520)]
    public void The_four_drags_switches_are_two_rows_of_two(double width)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new SpiralFeatureControl();
            var host = RowsRender.Host(page, width, show: new[] { "PoolRows" },
                before: p => ((MoreFold)p.FindName("FoldPoolMore")!).IsOpen = true);
            var chips = (UniformGrid)page.FindName("PoolDragsChips")!;
            var cells = chips.Children.OfType<StackPanel>().ToArray();
            Assert.Equal(4, cells.Length);

            double Y(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), host).Y;
            Assert.Equal(Y(cells[0]), Y(cells[1]), 1);
            Assert.Equal(Y(cells[2]), Y(cells[3]), 1);
            Assert.True(Y(cells[2]) > Y(cells[0]) + 10, "Bubbles and Words sit on the second row");

            foreach (var label in cells.SelectMany(c => c.Children.OfType<RowLabel>()))
                Assert.Null(RowsRender.Clipped(label));

            RowsRender.Dump(host, "whirlpool-drags-" + width.ToString("0"));
        });
    }
}
