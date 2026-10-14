using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AssetPackTreeTests
{
    [Fact]
    public void ImagesPrecedeVideosAndUseThePackSelectionKey()
    {
        var files = new[]
        {
            new PackFileEntry { OriginalName = "v.mp4", FileType = "video" },
            new PackFileEntry { OriginalName = "a.png", FileType = "image" },
            new PackFileEntry { OriginalName = "b.png", FileType = "image" }
        };
        var pack = AssetPackTree.Build("p1", "One", files,
            new HashSet<string> { "pack:p1/a.png", "pack:p1/v.mp4" })!;
        Assert.Equal("One", pack.Name);
        Assert.Equal(3, pack.FileCount);
        Assert.True(pack.IsPackFolder);
        Assert.False(pack.IsExpanded);
        Assert.Equal(new[] { "images", "videos" }, pack.Children.Select(n => n.Name));
        Assert.Equal(new[] { 2, 1 }, pack.Children.Select(n => n.FileCount));
        Assert.Equal(new[] { 1, 0 }, pack.Children.Select(n => n.CheckedFileCount));
        Assert.True(pack.IsChecked);
        Assert.True(pack.Children[0].IsChecked);
        Assert.False(pack.Children[1].IsChecked);
        Assert.All(pack.Children, n => Assert.Same(pack, n.Parent));
    }

    [Fact]
    public void EmptyPacksAreOmittedAndFullyDisabledPacksAreUnchecked()
    {
        Assert.Null(AssetPackTree.Build("p1", "One", new List<PackFileEntry>(), new HashSet<string>()));
        var pack = AssetPackTree.Build("p1", "One",
            new[] { new PackFileEntry { OriginalName = "a.png", FileType = "image" } },
            new HashSet<string> { "pack:p1/a.png" })!;
        Assert.False(pack.IsChecked);
        Assert.False(Assert.Single(pack.Children).IsChecked);
    }
}
