using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class HypnotubeTutorialSampleTests
{
    [Fact]
    public void SelectsTheFirstCaseInsensitiveTikTokTitleWithoutSorting()
    {
        var links = new Dictionary<string, string>
        {
            ["Unrelated title"] = "other",
            ["Z - TIKTOK sample"] = "first",
            ["A - TikTok sample"] = "second",
        };
        Assert.Equal("first", HypnotubeDefaultLinks.SelectTikTokTutorialVideo(links));
    }

    [Fact]
    public void MissingMatchingOrUnavailableTableUsesTheWpfFallback()
    {
        const string expected = "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html";
        Assert.Equal(expected, HypnotubeDefaultLinks.SelectTikTokTutorialVideo(null));
        Assert.Equal(expected, HypnotubeDefaultLinks.SelectTikTokTutorialVideo(new Dictionary<string, string>()));
        Assert.Equal(expected, HypnotubeDefaultLinks.SelectTikTokTutorialVideo(new Dictionary<string, string> { ["Other"] = "other" }));
        Assert.Equal(expected, HypnotubeDefaultLinks.SelectTikTokTutorialVideo(HypnotubeDefaultLinks.KnownVideoTitles));
    }
}
