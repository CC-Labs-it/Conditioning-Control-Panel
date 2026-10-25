using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DashboardClickRulesTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void HomeFeatureButtonsSwapNavigationAndToggle(bool right, bool inverted, bool toggle)
        => Assert.Equal(toggle, DashboardClickRules.IsToggle(right, true, true, inverted));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NavigationCardsAndOtherPagesKeepTheirButtons(bool dashboard, bool toggleable)
    {
        Assert.False(DashboardClickRules.IsToggle(false, dashboard, toggleable, true));
        Assert.True(DashboardClickRules.IsToggle(true, dashboard, toggleable, true));
    }

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public void HoverCloseLeavesPointerFocusAndPinnedInteractionsAlone(bool pinned, bool anchor, bool body, bool focus, bool close)
        => Assert.Equal(close, DashboardClickRules.ShouldClose(pinned, anchor, body, focus));

    [Fact]
    public void CaptionRetiresAfterThreeLearnedToggles()
    {
        Assert.True(DashboardToggleHintRule.ShouldShow(0));
        Assert.True(DashboardToggleHintRule.ShouldShow(2));
        Assert.False(DashboardToggleHintRule.ShouldShow(3));
        Assert.False(DashboardToggleHintRule.ShouldShow(100));
    }
}
