using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SideArtRulesTests
{
    [Theory]
    [InlineData(999, false, true)]
    [InlineData(1000, false, false)]
    [InlineData(1000, true, true)]
    [InlineData(1047, true, true)]
    [InlineData(1048, true, false)]
    public void ScrollbarWidthChangesDoNotRestoreCollapsedArt(double width, bool collapsed, bool expected)
        => Assert.Equal(expected, SideArtRules.ShouldCollapse(width, 1000, collapsed));
}
