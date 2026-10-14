using System.IO;
using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Just For You right-click "Open file" / "Show in folder" (ccp-bugs #1290).</summary>
public class FypFileRulesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ccp-fyp-root");

    [Fact]
    public void LibraryId_ResolvesUnderTheRoot()
    {
        var path = FypFileRules.ResolveLocal(Root, "videos/sub dir/clip one.mp4");
        Assert.Equal(Path.Combine(Root, "videos", "sub dir", "clip one.mp4"), path);
    }

    [Fact]
    public void LibraryGif_ResolvesUnderTheRoot()
    {
        var path = FypFileRules.ResolveLocal(Root, "images/a/b.gif");
        Assert.Equal(Path.Combine(Root, "images", "a", "b.gif"), path);
    }

    [Theory]
    [InlineData("videos\\sub dir\\clip.webm", "videos/sub dir/clip.webm")]
    [InlineData("images\\sub dir\\clip.gif", "images/sub dir/clip.gif")]
    public void WindowsSeparators_ResolveOnEitherHead(string id, string expected)
    {
        Assert.Equal(Path.Combine(Root, expected.Replace('/', Path.DirectorySeparatorChar)),
            FypFileRules.ResolveLocal(Root, id));
    }

    [Fact]
    public void OnlineId_HasNoFile()
    {
        Assert.Null(FypFileRules.ResolveLocal(Root, "scrolller/sissyhypno/abc"));
    }

    [Theory]
    [InlineData("../outside.mp4")]
    [InlineData("videos/../../outside.mp4")]
    [InlineData("C:/Windows/notepad.exe")]
    [InlineData("videos/a\".mp4")]
    [InlineData(".temp/remote.mp4")]
    [InlineData("videos/.cache/clip.mp4")]
    [InlineData("videos/run.exe")]
    [InlineData("videos/shortcut.lnk")]
    [InlineData("images/still.png")]
    [InlineData("other/clip.mp4")]
    [InlineData("clip.mp4")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsafeIds_AreRefused(string? id)
    {
        Assert.Null(FypFileRules.ResolveLocal(Root, id));
    }

    [Fact]
    public void SiblingFolderWithTheSamePrefix_IsRefused()
    {
        // "ccp-fyp-root2" starts with "ccp-fyp-root"; the separator check must catch it.
        Assert.Null(FypFileRules.ResolveLocal(Root, "../ccp-fyp-root2/x.mp4"));
    }
}
