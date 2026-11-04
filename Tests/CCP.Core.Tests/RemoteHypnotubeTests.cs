using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class RemoteHypnotubeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("https://hypnotube.com/")]
    [InlineData("https://hypnotube.com.evil.test/video/123")]
    [InlineData("file://hypnotube.com/video/123")]
    public void Rejects_ineligible_controller_links(string url)
    {
        Assert.Equal("not an eligible HypnoTube URL", RemoteCommands.Execute("play_hypnotube", new JObject { ["url"] = url }));
    }

    [Theory]
    [InlineData("https://hypnotube.com/video/123")]
    [InlineData("https://www.hypnotube.com/video/example-123.html")]
    public void Eligible_links_are_still_refused_until_playback_exists(string url)
    {
        Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("play_hypnotube", new JObject { ["url"] = url }));
    }
}
