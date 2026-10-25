using System;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BrowserPopupRuleTests
{
    [Theory]
    [InlineData("https://bambicloud.com/", "https://example.test/watch", true)]
    [InlineData("https://bambicloud.com/", "https://sub.doubleclick.net/watch", false)]
    [InlineData("https://bambicloud.com/", "https://example.test/ADS/watch", false)]
    [InlineData("https://bambicloud.com/", "https://example.test/popup", false)]
    [InlineData("https://bambicloud.com/", "https://example.test/clicktrack", false)]
    [InlineData("https://bambicloud.com/", "https://notdoubleclick.net/watch", false)]
    [InlineData("https://bambicloud.com/", "https://notexoclick.com/watch", true)]
    [InlineData("https://hypnotube.com/", "https://doubleclick.net/popup", true)]
    [InlineData("https://www.hypnotube.com/", "https://example.test/ads/watch", true)]
    [InlineData("https://nothypnotube.com/", "https://doubleclick.net/popup", false)]
    public void MatchesWpfPartnerAndAdPolicy(string current, string target, bool expected) =>
        Assert.Equal(expected, BrowserPopupRule.NavigateInPlace(new Uri(current), new Uri(target)));

    [Fact]
    public void NoCurrentPageStillFiltersAds()
    {
        Assert.False(BrowserPopupRule.NavigateInPlace(null, new Uri("https://exoclick.com/")));
        Assert.True(BrowserPopupRule.NavigateInPlace(null, new Uri("https://example.test/")));
        Assert.False(BrowserPopupRule.NavigateInPlace(null, new Uri("relative", UriKind.Relative)));
    }
}
