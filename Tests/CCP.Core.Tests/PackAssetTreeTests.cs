using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Content;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PackAssetTreeTests
{
    [Fact]
    public void TreeKeepsWpfOrderAndNamesAndCountsDisabledFiles()
    {
        var disabled = new HashSet<string> { "pack:p/a.png", "pack:p/v.mp4" };
        var tree = PackAssetTree.Build("p", "Pack", new[]
            { ("v.mp4", "video"), ("a.png", "image"), ("b.png", "image") }, disabled)!;
        Assert.Equal("Pack", tree.Name);
        Assert.Equal("p", tree.PackId);
        Assert.True(tree.IsPackFolder);
        Assert.Empty(tree.FullPath);
        Assert.Equal(3, tree.FileCount);
        Assert.Equal(new[] { "images", "videos" }, tree.Children.Select(c => c.Name));
        Assert.Equal(new[] { 2, 1 }, tree.Children.Select(c => c.FileCount));
        Assert.Equal(new[] { 1, 0 }, tree.Children.Select(c => c.CheckedFileCount));
        Assert.True(tree.IsChecked);
        Assert.False(tree.Children[1].IsChecked);
        Assert.All(tree.Children, c => Assert.Same(tree, c.Parent));
    }

    [Fact]
    public void SelectionUsesPackKeysAndLeavesLocalAndOtherPackSelectionsAlone()
    {
        var disabled = new HashSet<string> { "images/a.png", "pack:other/a.png" };
        PackAssetTree.SetSelection("p", new[] { "a.png", "sub/b.png" }, disabled, false);
        Assert.Contains("pack:p/sub/b.png", disabled);
        Assert.Equal(4, disabled.Count);
        PackAssetTree.SetSelection("p", new[] { "a.png", "sub/b.png" }, disabled, true);
        Assert.Equal(new[] { "images/a.png", "pack:other/a.png" }, disabled.OrderBy(x => x));
    }

    [Fact]
    public void EmptyPacksAreOmittedAndEntirelyDisabledPackIsUnchecked()
    {
        Assert.Null(PackAssetTree.Build("p", "Empty", System.Array.Empty<(string, string)>(), new HashSet<string>()));
        var tree = PackAssetTree.Build("p", "Off", new[] { ("a.png", "image") }, new HashSet<string> { "pack:p/a.png" })!;
        Assert.False(tree.IsChecked);
        Assert.Single(tree.Children);
    }
}
