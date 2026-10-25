using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The shared "via Scrolller" rules (WPF ScrolllerCreditSurfaceTests / ScrolllerCreditTests cases,
/// with Linux-shaped paths): what counts as Scrolller media, and when the credit link may open.</summary>
public class ScrolllerCreditRulesTests
{
    [Fact]
    public void Mark_IsTheExactBrandText() => Assert.Equal("via Scrolller", ScrolllerCreditRules.Mark);

    [Theory]
    [InlineData("https://images.scrolller.com/abc/def-1080.webp")]
    [InlineData("HTTPS://IMAGES.SCROLLLER.COM/x.jpg")]
    [InlineData("/home/me/assets/.temp/ccp_temp_remote_0f1e2d3c.webp")]
    [InlineData("/lib/.temp/CCP_TEMP_REMOTE_ab.mp4")]
    [InlineData("scrolller/gifs/25505178")]
    public void ScrolllerMedia_GetsTheMark(string path) => Assert.True(ScrolllerCreditRules.IsScrolllerMedia(path));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("/home/me/Pictures/flash.jpg")]
    [InlineData("/home/me/assets/.temp/ccp_temp_9a8b7c.mp4")]          // a content-pack decrypt, not online
    [InlineData("/home/me/assets/ccp_temp_remote_notes/a.jpg")]        // the prefix on a folder, not the file
    [InlineData("https://example.com/a.jpg")]
    [InlineData("https://notscrolller.com/x.jpg")]
    [InlineData("https://example.com/ccp_temp_remote_x.jpg")]          // a url is judged by its host only
    [InlineData("local/gifs/1")]
    [InlineData("scrolller/a b/1")]
    public void OtherMedia_NeverGetsTheMark(string? path) => Assert.False(ScrolllerCreditRules.IsScrolllerMedia(path));

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void Link_NeverOpensUnderALock(bool strict, bool lockdown, bool chaos, bool open)
        => Assert.Equal(open, ScrolllerCreditRules.CanOpenLink(strict, lockdown, chaos));
}
