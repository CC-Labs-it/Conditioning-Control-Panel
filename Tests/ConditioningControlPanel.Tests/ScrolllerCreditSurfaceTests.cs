using System;
using System.IO;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "via Scrolller" on every native surface (owner, 2026-10-17): the pure rule that says whether a
/// path or url is Scrolller's, and the wiring that holds the mark only while such an item is up.
/// </summary>
public class ScrolllerCreditSurfaceTests
{
    [Fact]
    public void Mark_IsTheExactBrandText() => Assert.Equal("via Scrolller", ScrolllerCredit.Mark);

    [Theory]
    [InlineData("https://images.scrolller.com/abc/def-1080.webp")]
    [InlineData("https://v.scrolller.com/clip.mp4")]
    [InlineData("https://scrolller.com/r/gifs")]
    [InlineData("http://api.scrolller.com/admin")]
    [InlineData("HTTPS://IMAGES.SCROLLLER.COM/x.jpg")]
    public void ScrolllerUrls_AreScrolllers(string url)
    {
        Assert.True(ScrolllerCredit.IsScrolllerUrl(url));
        Assert.True(ScrolllerCredit.IsScrolllerMedia(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://notscrolller.com/x.jpg")]
    [InlineData("https://scrolller.com.evil.net/x.jpg")]
    [InlineData("https://example.com/scrolller.com/x.jpg")]
    [InlineData("ftp://images.scrolller.com/x.jpg")]
    [InlineData("scrolller.com/x.jpg")]
    public void LookAlikes_AreNotScrolllerUrls(string? url) => Assert.False(ScrolllerCredit.IsScrolllerUrl(url));

    [Theory]
    [InlineData(@"D:\assets\.temp\ccp_temp_remote_0f1e2d3c.webp")]
    [InlineData("C:/lib/.temp/CCP_TEMP_REMOTE_ab.mp4")]
    [InlineData("scrolller/gifs/25505178")]
    public void TempCopiesAndEntryIds_AreScrolllers(string path) => Assert.True(ScrolllerCredit.IsScrolllerMedia(path));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData(@"C:\Users\me\Pictures\flash.jpg")]
    [InlineData(@"D:\assets\.temp\ccp_temp_9a8b7c.mp4")]          // a content-pack decrypt, not online
    [InlineData(@"D:\assets\images\ccp_temp_remote_notes\a.jpg")] // the prefix on a folder, not the file
    [InlineData("https://example.com/a.jpg")]
    [InlineData("local/gifs/1")]
    public void LocalAndOtherMedia_NeverGetTheMark(string? path) => Assert.False(ScrolllerCredit.IsScrolllerMedia(path));

    [Fact]
    public void TempPrefix_StaysInStepWithRemoteMediaCache()
    {
        var src = Read("ConditioningControlPanel", "Services", "Fyp", "Online", "RemoteMediaCache.cs");
        Assert.Contains("TempPrefix = \"" + ScrolllerCredit.RemoteTempPrefix + "\"", src);
    }

    [Theory]
    [InlineData("Chaos/ChaosFlashOverlay.cs")]
    [InlineData("Chaos/ChaosGifCascadeOverlay.cs")]
    [InlineData("Services/BubbleService.cs")]
    public void ManyItemSurfaces_HoldAndReleaseTheChip(string file)
    {
        var src = Read(("ConditioningControlPanel/" + file).Split('/'));
        Assert.Contains("ScrolllerCredit.IsScrolllerMedia(", src);
        Assert.Contains("ScrolllerCreditChip.Hold(", src);
        Assert.Contains("ScrolllerCreditChip.Release(", src);
    }

    [Fact]
    public void Flashes_CarryTheCreditOnThePictureItself()
    {
        // Owner, 2026-10-17: the corner chip was missed on flashes; the tag goes on the picture.
        var flash = Read("ConditioningControlPanel", "Services", "Flash", "FlashService.cs");
        Assert.Contains("window.LayerItem.Credit = Video.ScrolllerCredit.IsScrolllerMedia(", flash);
        Assert.Contains("new Controls.ScrolllerCreditBadge", flash);
        Assert.DoesNotContain("ScrolllerCreditChip.Hold(", flash);
        var layer = Read("ConditioningControlPanel", "Services", "Compositor", "FlashLayer.cs");
        Assert.Contains("if (item.Credit && item.Exit == null) DrawCredit(", layer);
        Assert.Contains("canvas.DrawText(Video.ScrolllerCredit.Mark", layer);
    }

    [Fact]
    public void Badge_NeverTakesInputAndUsesTheBrandText()
    {
        var src = Read("ConditioningControlPanel", "Controls", "ScrolllerCreditMark.cs");
        Assert.Contains("IsHitTestVisible = false", src);
        Assert.Contains("Text = ScrolllerCredit.Mark", src);
        Assert.Contains("GentleVisuals.FadeMs(", src);
    }

    [Fact]
    public void CreditLine_IsInEveryLanguageFile()
    {
        var dir = Path.Combine(Root(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            Assert.Contains("\"label_online_media_credit\"", text);
            Assert.Contains("Scrolller", text);
        }
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(Root(), Path.Combine(parts));
        Assert.True(File.Exists(path), $"product source missing: {path}");
        return File.ReadAllText(path);
    }
}
