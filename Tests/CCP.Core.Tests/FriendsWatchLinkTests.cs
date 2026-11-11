using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class FriendsWatchLinkTests
{
    [Fact]
    public void WatchLinksUsePlayableSlugsAndPreserveTheWireIdGrammar()
    {
        foreach (var id in new[] { "109749", "0", "00012345", "12345678" })
        {
            var url = LandingRules.HtUrl(id);
            // Hypnotube returns 404 for the old bare /video/<id> shape.
            Assert.Equal("https://hypnotube.com/video/video-" + id + ".html", url);
            Assert.True(HtUrlHelper.IsEligibleHtUrl(url));
            Assert.Equal(id, HtUrlHelper.TryExtractHtVideoId(url));
        }
        foreach (var id in new string?[] { null, "", "12a", "123456789", " 123", "123/456", "١٢٣" })
            Assert.Null(LandingRules.HtUrl(id));
    }
}
