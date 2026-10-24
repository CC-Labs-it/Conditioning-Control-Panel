using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ScrolllerCreditIdentityTests
{
    [Theory]
    [InlineData("https://images.scrolller.com/picture.jpg", true)]
    [InlineData("https://scrolller.com/r/art", true)]
    [InlineData("scrolller/art/post", true)]
    [InlineData("ccp_temp_remote_picture.jpg", true)]
    [InlineData("CCP_TEMP_REMOTE_picture.jpg", true)]
    [InlineData("https://notscrolller.com/picture.jpg", false)]
    [InlineData("https://example.com/ccp_temp_remote_picture.jpg", false)]
    [InlineData("scrolller/a/post", false)]
    [InlineData("scrolller/art/extra/post", false)]
    [InlineData("local.jpg", false)]
    [InlineData("ccp_pack_picture.jpg", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void OnlyScrolllerMediaCarriesTheMark(string? path, bool expected) =>
        Assert.Equal(expected, ScrolllerCredit.IsScrolllerMedia(path));
}
