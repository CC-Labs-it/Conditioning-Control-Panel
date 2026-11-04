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
    public void ReadFailureMeansDoNotNag() => Assert.True(LocalMediaLibrary.FolderHasAnyMedia(
        _assets, new[] { ".png" }, _ => throw new IOException("test unreadable library")));

    public void Dispose() => Directory.Delete(_assets, recursive: true);
}
