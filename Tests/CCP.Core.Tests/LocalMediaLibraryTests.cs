using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class LocalMediaLibraryTests : IDisposable
{
    private readonly string _assets = Directory.CreateTempSubdirectory("ccp-empty-library-").FullName;

    [Theory]
    [InlineData("local", true)]
    [InlineData("LOCAL", true)]
    [InlineData("online", false)]
    [InlineData("mixed", false)]
    public void OnlyLocalFlashStartsReportMissingImages(string source, bool expected)
    {
        var s = new AppSettings { FlashEnabled = true, MediaSource = source };
        Assert.Equal(expected, LocalMediaLibrary.FlashIsEmpty(s, _assets));
        s.AudioOnlySession = true;
        Assert.False(LocalMediaLibrary.FlashIsEmpty(s, _assets));
        s.AudioOnlySession = false; s.FlashEnabled = false;
        Assert.False(LocalMediaLibrary.FlashIsEmpty(s, _assets));
    }

    [Fact]
    public void NestedImageExtensionsCountWithoutRequiringADecode()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_assets, "images", "nested")).FullName;
        var s = new AppSettings { FlashEnabled = true, MediaSource = "local" };
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not media");
        Assert.True(LocalMediaLibrary.FlashIsEmpty(s, _assets));
        File.WriteAllText(Path.Combine(dir, "photo.JPEG"), "extension is enough for the WPF probe");
        Assert.False(LocalMediaLibrary.FlashIsEmpty(s, _assets));
    }

    [Fact]
    public void WallpaperUsesItsExistingCustomFolderAndRejectsGifAndWebp()
    {
        var fallback = Directory.CreateDirectory(Path.Combine(_assets, "wallpapers")).FullName;
        File.WriteAllText(Path.Combine(fallback, "photo.bmp"), "");
        var custom = Directory.CreateDirectory(Path.Combine(_assets, "custom")).FullName;
        var s = new AppSettings { MediaSource = "local", WallpaperSourceFolder = custom };
        File.WriteAllText(Path.Combine(custom, "animated.gif"), "");
        File.WriteAllText(Path.Combine(custom, "photo.webp"), "");
        Assert.True(LocalMediaLibrary.WallpaperIsEmpty(s, _assets));
        File.WriteAllText(Path.Combine(custom, "photo.PNG"), "");
        Assert.False(LocalMediaLibrary.WallpaperIsEmpty(s, _assets));
        s.WallpaperSourceFolder = Path.Combine(_assets, "missing");
        Assert.False(LocalMediaLibrary.WallpaperIsEmpty(s, _assets));
        s.MediaSource = "mixed";
        Assert.False(LocalMediaLibrary.WallpaperIsEmpty(s, _assets));
    }

    [Fact]
    public void ReadFailureMeansDoNotNag() => Assert.True(LocalMediaLibrary.FolderHasAnyMedia(
        _assets, new[] { ".png" }, _ => throw new IOException("test unreadable library")));

    public void Dispose() => Directory.Delete(_assets, recursive: true);
}
