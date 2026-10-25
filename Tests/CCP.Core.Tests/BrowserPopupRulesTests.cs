using System;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BrowserPopupRulesTests
{
    [Theory]
    [InlineData("https://bambicloud.com/", "https://sub.doubleclick.net/", false)]
    [InlineData("https://bambicloud.com/", "https://example.test/ads/file", false)]
    [InlineData("https://bambicloud.com/", "https://example.test/popup", false)]
    [InlineData("https://bambicloud.com/", "https://example.test/video", true)]
    [InlineData("https://hypnotube.com/", "https://doubleclick.net/", true)]
    [InlineData("https://www.hypnotube.com/", "https://example.test/popup", true)]
    [InlineData("https://hypnotube.com.evil.test/", "https://doubleclick.net/", false)]
    [InlineData("https://bambicloud.com/", "https://notdoubleclick.net/", false)]
    public void MatchesWpfPartnerBypassAndAdPatterns(string current, string target, bool expected) =>
        Assert.Equal(expected, BrowserPopupRules.Allows(new Uri(current), new Uri(target)));

    [Fact]
    public void MissingOrRelativeDestinationIsNotNavigable()
    {
        Assert.False(BrowserPopupRules.Allows(null, null));
        Assert.False(BrowserPopupRules.Allows(null, new Uri("relative", UriKind.Relative)));
        Assert.True(BrowserPopupRules.Allows(null, new Uri("https://example.test/")));
    }
}
