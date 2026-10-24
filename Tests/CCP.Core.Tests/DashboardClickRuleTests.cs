using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DashboardClickRuleTests
{
    [Theory]
    [InlineData(false, false, true, true, false)]
    [InlineData(true, false, true, true, true)]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, true, true, false, true)]
    public void OnlyDashboardToggleTilesInvert(bool right, bool invert, bool dashboard, bool toggleable, bool expected)
        => Assert.Equal(expected, DashboardClickRule.ShouldToggle(right, invert, dashboard, toggleable));
}
