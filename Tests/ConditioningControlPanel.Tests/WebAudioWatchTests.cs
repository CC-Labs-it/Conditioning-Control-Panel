using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1549: a page playing a clip's sound must reach the recent-sounds ring under a name a
/// report can be read by: the host names the surface, the last path segment the page.
/// </summary>
public class WebAudioWatchTests
{
    [Fact]
    public void A_page_is_named_by_its_host_and_file()
    {
        var (player, file) = WebAudioWatch.Describe("https://ccp.race3d/index.html?x=1", muted: false);
        Assert.Equal("web:ccp.race3d", player);
        Assert.Equal("index.html", file);
    }

    [Fact]
    public void A_muted_page_says_so()
    {
        var (player, _) = WebAudioWatch.Describe("https://ccp.backroom/", muted: true);
        Assert.Equal("web:ccp.backroom:muted", player);
    }

    [Fact]
    public void A_missing_source_still_names_something()
    {
        var (player, file) = WebAudioWatch.Describe(null, muted: false);
        Assert.Equal("web:page", player);
        Assert.Equal("(page)", file);
    }
}
