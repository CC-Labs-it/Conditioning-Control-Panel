using ConditioningControlPanel.Helpers;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The side-art fold rule both heads' AdaptiveSideArt share (ccp-bugs #1321 hysteresis).</summary>
public class SideArtCollapseTests
{
    [Theory]
    [InlineData(999, false, true)]    // wide page dips under the threshold: collapse
    [InlineData(1000, false, false)]  // at the threshold: stay wide
    [InlineData(1010, true, true)]    // collapsed, and only a scrollbar wider: hold
    [InlineData(1047, true, true)]    // still inside the margin: hold
    [InlineData(1048, true, false)]   // threshold + WidenMargin: the art comes back
    public void CollapsesBelowAndWidensOnlyPastTheMargin(double width, bool collapsedNow, bool expected)
        => Assert.Equal(expected, SideArtCollapse.ShouldCollapse(width, 1000, collapsedNow));
}
