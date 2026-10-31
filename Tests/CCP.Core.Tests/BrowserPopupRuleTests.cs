using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace CCP.Core.Tests;

public class BrowserPopupRuleTests
{
    [Theory]
    [InlineData("https://ads.doubleclick.net/watch")]
    [InlineData("https://EXOCLICK.COM/watch")]
    [InlineData("https://example.com/ads/watch")]
    [InlineData("https://example.com/ad/watch")]
    [InlineData("https://example.com/popup")]
    [InlineData("https://example.com/clicktrack")]
    [InlineData("https://example.com/adserver")]
    [InlineData("https://example.com/doubleclick")]
    [InlineData("https://example.com/googlesyndication")]
    public void BlocksWpfAdDomainsAndPatterns(string target) =>
        Assert.True(BrowserPopupRule.ShouldBlock("https://bambicloud.com/", target));

    [Theory]
    [InlineData("https://hypnotube.com/")]
    [InlineData("https://WWW.HYPNOTUBE.COM/watch")]
    public void PartnerDocumentBypassesAdPolicy(string page) =>
        Assert.False(BrowserPopupRule.ShouldBlock(page, "https://doubleclick.net/ads/popup"));

    [Theory]
    [InlineData("https://nothypnotube.com/")]
    [InlineData("https://hypnotube.com.example.org/")]
    public void PartnerNameMustMatchHostBoundary(string page) =>
        Assert.True(BrowserPopupRule.ShouldBlock(page, "https://doubleclick.net/"));

    [Theory]
    [InlineData("https://notexoclick.com.example/watch")]
    [InlineData("https://example.org/watch")]
    [InlineData("not a URI")]
    public void LeavesOrdinaryAndUnparseableTargetsAlone(string target) =>
        Assert.False(BrowserPopupRule.ShouldBlock(null, target));
}
