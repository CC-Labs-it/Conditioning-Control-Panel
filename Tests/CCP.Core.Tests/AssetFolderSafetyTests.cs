using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AssetFolderSafetyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\0invalid")]
    public void MissingAndUnresolvablePathsAreRefused(string? path)
        => Assert.True(AssetFolderSafety.IsPersonalFolderRoot(path));

    [Fact]
    public void WholeDriveAndPersonalRootsAreRefusedButDedicatedChildrenAreAllowed()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(AssetFolderSafety.IsPersonalFolderRoot(Path.GetPathRoot(Path.GetFullPath("."))));
        Assert.True(AssetFolderSafety.IsPersonalFolderRoot(home));
        Assert.True(AssetFolderSafety.IsPersonalFolderRoot(Path.Combine(home, "Pictures")));
        Assert.False(AssetFolderSafety.IsPersonalFolderRoot(Path.Combine(home, "ccp media")));
        Assert.False(AssetFolderSafety.IsPersonalFolderRoot(Path.Combine(home, "Pictures", "my-media")));
    }
    [Theory]
    [InlineData("OneDrive")]
    [InlineData("OneDriveConsumer")]
    [InlineData("OneDriveCommercial")]
    public void OneDriveRootsAndKnownFoldersAreRefusedWithoutBlockingDedicatedChildren(string variable)
    {
        var before = Environment.GetEnvironmentVariable(variable);
        var root = Path.Combine(Path.GetTempPath(), "ccp-onedrive-rule-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(variable, root);
            Assert.True(AssetFolderSafety.IsPersonalFolderRoot(root));
            Assert.True(AssetFolderSafety.IsPersonalFolderRoot(Path.Combine(root, "Pictures")));
            Assert.False(AssetFolderSafety.IsPersonalFolderRoot(Path.Combine(root, "Pictures", "my-media")));
        }
        finally { Environment.SetEnvironmentVariable(variable, before); }
    }

}
