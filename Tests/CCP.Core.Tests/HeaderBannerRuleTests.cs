using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Declutter wave 1 (2026-11-15): the header banner is Home's alone (WPF BannerFxRules.ShowsOnTab).</summary>
public class HeaderBannerRuleTests
{
    [Theory]
    [InlineData("settings", true)]
    [InlineData("Settings", true)]
    [InlineData("progression", true)]   // permanent alias that lands on the dashboard
    [InlineData(null, true)]            // nothing navigated yet = the dashboard the app lands on
    [InlineData("", true)]
    [InlineData("appsettings", false)]  // the Settings PAGE is not Home
    [InlineData("studio", false)]
    [InlineData("play", false)]
    [InlineData("companion", false)]
    [InlineData("quests", false)]
    [InlineData("chaster", false)]
    [InlineData("friends", false)]
    public void TheBannerShowsOnHomeOnly(string? tab, bool shows) =>
        Assert.Equal(shows, HeaderBannerRule.ShowsOnTab(tab));
}
