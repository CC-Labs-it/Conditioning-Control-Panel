using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace CCP.Core.Tests;

public class WebPageSoundTests
{
    [Theory]
    [InlineData("https://ccp.race3d/index.html?x=1", false, "web:ccp.race3d", "index.html")]
    [InlineData("https://ccp.backroom/", true, "web:ccp.backroom:muted", "(page)")]
    [InlineData(null, false, "web:page", "(page)")]
    public void NamesPages(string? source, bool muted, string player, string file)
    {
        Assert.Equal((player, file), WebPageSound.Describe(source, muted));
    }
}
