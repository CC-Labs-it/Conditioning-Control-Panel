using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ModCatalogueAssetTests
{
    [Fact]
    public void MetadataUsesCreatorHostedDownloadAndRecursiveInstalledSize()
    {
        var root = Directory.CreateTempSubdirectory("ccp-mod-envelope-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "mod.json"), new byte[3]);
            Directory.CreateDirectory(Path.Combine(root, "resources"));
            File.WriteAllBytes(Path.Combine(root, "resources", "art.png"), new byte[7]);
            var mod = new ModPackage(new ModManifest
            {
                Id = "my-mod", Name = "My Mod", Version = "2.0", Author = "creator",
                Description = "Description", MinAppVersion = "7.1.5",
            }, root, false);
            var asset = ModCatalogueAsset.Build(mod, "https://mega.nz/file/test#key", "thumbnail");
            Assert.Equal("my-mod", (string?)asset["mod_id"]);
            Assert.Equal("My Mod", (string?)asset["name"]);
            Assert.Equal("2.0", (string?)asset["version"]);
            Assert.Equal("creator", (string?)asset["author"]);
            Assert.Equal("Description", (string?)asset["description"]);
            Assert.Equal("7.1.5", (string?)asset["min_app_version"]);
            Assert.Equal("mega", (string?)asset["download"]!["provider"]);
            Assert.Equal("https://mega.nz/file/test#key", (string?)asset["download"]!["url"]);
            Assert.Equal(10, (long)asset["download"]!["installed_size_bytes"]!);
            Assert.Equal("thumbnail", (string?)asset["preview_thumb_b64"]);
            Assert.Null(asset["sha256"]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingDirectoryReturnsZeroAndEmptyOptionalMetadataIsOmitted()
    {
        var mod = new ModPackage(new ModManifest { Description = " ", MinAppVersion = "" },
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), false);
        var asset = ModCatalogueAsset.Build(mod, "url");
        Assert.Equal(0, (long)asset["download"]!["installed_size_bytes"]!);
        Assert.Null(asset["description"]);
        Assert.Null(asset["min_app_version"]);
        Assert.Null(asset["preview_thumb_b64"]);
    }
}
