using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AssetPackTreeTests
{
    [Fact]
    public void PackLeavesKeepWpfOrderAndSelectionKeys()
    {
        var files = new[]
        {
            new PackFileEntry { OriginalName = "v.mp4", FileType = "video" },
            new PackFileEntry { OriginalName = "a.png", FileType = "image" },
            new PackFileEntry { OriginalName = "b.png", FileType = "image" }
        };
        var node = AssetPackTree.Build("p", "Pack", files, new HashSet<string> { "pack:p/a.png", "pack:p/v.mp4" })!;
        Assert.Equal("Pack", node.Name);
        Assert.Equal(new[] { "images", "videos" }, node.Children.Select(n => n.Name));
        Assert.Equal(3, node.FileCount);
        Assert.Equal(1, node.GetTotalCheckedFileCount());
        Assert.True(node.IsChecked);
        Assert.True(node.Children[0].IsChecked);
        Assert.False(node.Children[1].IsChecked);
        Assert.All(node.Children, child => { Assert.Same(node, child.Parent); Assert.Equal("", child.FullPath); Assert.Equal("p", child.PackId); });
        Assert.Null(AssetPackTree.Build("empty", "Empty", new List<PackFileEntry>(), new HashSet<string>()));
    }
}
