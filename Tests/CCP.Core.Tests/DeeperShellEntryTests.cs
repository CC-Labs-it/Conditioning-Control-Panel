using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class DeeperShellEntryTests
{
    [Fact]
    public void FileOpenUsesFirstFlagAndRejectsMissingUnsupportedAndNetworkPaths()
    {
        using var dir = new TempFolder();
        var media = Path.Combine(dir.Path, "sample.MP4");
        File.WriteAllText(media, "test");
        Assert.Equal(("edit", media), DeeperFileOpen.Parse(new[] { "--edit", media, "--play", media }));
        Assert.Equal(("play", media), DeeperFileOpen.Parse(new[] { "--play", media }));
        Assert.Equal(("edit", media), DeeperFileOpen.Decode(DeeperFileOpen.Encode(new[] { "--edit", media })));
        Assert.Null(DeeperFileOpen.Decode("deeper:destroy\n" + media).Action);
        Assert.Null(DeeperFileOpen.Decode("game:race").Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--PLAY", media }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--edit" }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--play", media + ".missing" }).Action);
        Assert.Null(DeeperFileOpen.Parse(new[] { "--play", @"\\server\sample.mp4" }).Action);
        var text = Path.Combine(dir.Path, "sample.txt");
        File.WriteAllText(text, "test");
        Assert.Null(DeeperFileOpen.Parse(new[] { "--edit", text }).Action);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("deeper-entry-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
