using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AdaptiveSideArtRuleTests
{
    [Theory]
    [InlineData(859, false, true)]
    [InlineData(860, false, false)]
    [InlineData(860, true, true)]
    [InlineData(907, true, true)]
    [InlineData(908, true, false)]
    public void BlinkThresholdAndWidenMargin(double width, bool collapsed, bool expected)
        => Assert.Equal(expected, AdaptiveSideArtRule.ShouldCollapse(width, 860, collapsed));

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(17)]
    public void ScrollbarAppearingAndDisappearingSettlesWithinOneChange(double scrollbar)
    {
        for (double viewport = 800; viewport <= 940; viewport++)
        foreach (bool initial in new[] { false, true })
        {
            bool collapsed = initial;
            int changes = 0;
            for (int pass = 0; pass < 10; pass++)
            {
                var next = AdaptiveSideArtRule.ShouldCollapse(collapsed ? viewport : viewport - scrollbar, 860, collapsed);
                if (next != collapsed) changes++;
                collapsed = next;
            }
            Assert.True(changes <= 1, $"Viewport {viewport}, scrollbar {scrollbar}: {changes} changes");
        }
    }
}
