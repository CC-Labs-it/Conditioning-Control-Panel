using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

// ccp-bugs #1693: BambiCloud's menu lost its top line in a non-maximised window.
public class BambiCloudHeaderFitTests
{
    [Theory]
    [InlineData("https://bambicloud.com/", true)]
    [InlineData("https://www.bambicloud.com/playlist/123", true)]
    [InlineData("https://BambiCloud.com/wiki", true)]
    [InlineData("https://hypnotube.com/", false)]
    [InlineData("https://notbambicloud.com/", false)]
    [InlineData("https://bambicloud.com.example.net/", false)]
    [InlineData("file:///C:/bambicloud.com/index.html", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyBambiCloudPagesGetTheFit(string? url, bool expected)
        => Assert.Equal(expected, BambiCloudHeaderFit.AppliesTo(url));

    [Fact]
    public void HeaderRowGrowsAndTheTotalStaysOneScreen()
    {
        var css = BambiCloudHeaderFit.Css;
        // Only the wide grid layout; the site's 1000 px and under flex column is left alone.
        Assert.StartsWith("@media (width > 1000px){", css);
        // Header row may grow past 13vh, the content row takes the difference, player keeps 10vh.
        Assert.Contains("grid-template-rows:minmax(13vh,max-content) minmax(0,1fr) 10vh", css);
        Assert.Contains("grid-template-rows:minmax(13vh,max-content) minmax(0,1fr)}", css);
        Assert.Contains("height:100vh", css);
    }

    [Fact]
    public void ScriptIsIdempotentAndCarriesTheCssIntact()
    {
        var script = BambiCloudHeaderFit.Script;
        Assert.Contains("getElementById('" + BambiCloudHeaderFit.StyleId + "')", script);
        Assert.Contains(BambiCloudHeaderFit.Css, script);
        // The CSS sits inside a single-quoted JS string.
        Assert.DoesNotContain("'", BambiCloudHeaderFit.Css);
        Assert.DoesNotContain("\\", BambiCloudHeaderFit.Css);
    }
}
