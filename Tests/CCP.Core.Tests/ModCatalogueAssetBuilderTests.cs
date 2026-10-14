using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using SkiaSharp;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ModCatalogueAssetBuilderTests
{
    [Fact]
    public void Metadata_contains_hosted_link_directory_size_and_optional_fields_without_a_binary_or_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-mod-share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "one"), new byte[3]);
            File.WriteAllBytes(Path.Combine(root, "nested", "two"), new byte[4]);
            var manifest = new ModManifest { Id = "mod", Name = "Name", Version = "1.2", Author = "Author", Description = "Description", MinAppVersion = "7.1.5" };
            var asset = ModCatalogueAssetBuilder.Build(new ModPackage(manifest, root, false), "https://mega.nz/file/id#key");
            Assert.Equal("ccp-mod/v1", ModCatalogueAssetBuilder.Schema);
            Assert.Equal("mod", (string?)asset["mod_id"]);
            Assert.Equal("Name", (string?)asset["name"]);
            Assert.Equal("1.2", (string?)asset["version"]);
            Assert.Equal("Author", (string?)asset["author"]);
            Assert.Equal("Description", (string?)asset["description"]);
            Assert.Equal("7.1.5", (string?)asset["min_app_version"]);
            Assert.Equal("mega", (string?)asset["download"]!["provider"]);
            Assert.Equal("https://mega.nz/file/id#key", (string?)asset["download"]!["url"]);
            Assert.Equal(7, (long)asset["download"]!["installed_size_bytes"]!);
            Assert.Null(asset["sha256"]);
            Assert.Null(asset["binary"]);
            Assert.Null(asset["preview_thumb_b64"]);
            manifest.Description = " "; manifest.MinAppVersion = "";
            var missing = ModCatalogueAssetBuilder.Build(new ModPackage(manifest, root + "-missing", false), "url");
            Assert.Equal(0, (long)missing["download"]!["installed_size_bytes"]!);
            Assert.Null(missing["description"]); Assert.Null(missing["min_app_version"]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Preview_is_a_160_pixel_wide_jpeg_and_unusable_or_over_budget_art_is_omitted()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-mod-preview-" + Guid.NewGuid().ToString("N"));
        var resources = Path.Combine(root, "resources");
        Directory.CreateDirectory(resources);
        try
        {
            var path = Path.Combine(resources, "preview.png");
            var mod = new ModPackage(new ModManifest { PreviewImage = "preview.png" }, root, false);
            Assert.Null(ModCatalogueAssetBuilder.Build(mod, "url")["preview_thumb_b64"]);
            File.WriteAllText(path, "invalid image");
            Assert.Null(ModCatalogueAssetBuilder.Build(mod, "url")["preview_thumb_b64"]);
            using (var bitmap = new SKBitmap(400, 200))
            {
                bitmap.Erase(SKColors.Magenta);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, png.ToArray());
            }
            var bytes = Convert.FromBase64String((string)ModCatalogueAssetBuilder.Build(mod, "url")["preview_thumb_b64"]!);
            Assert.True(bytes.Length <= 64 * 1024);
            Assert.Equal(0xFF, bytes[0]); Assert.Equal(0xD8, bytes[1]);
            using var preview = SKBitmap.Decode(bytes);
            Assert.Equal(160, preview.Width); Assert.Equal(80, preview.Height);
            using (var noise = new SKBitmap(160, 1000))
            {
                var rng = new Random(123);
                var colors = new SKColor[160 * 1000];
                for (var i = 0; i < colors.Length; i++) colors[i] = new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
                noise.Pixels = colors;
                using var image = SKImage.FromBitmap(noise);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, png.ToArray());
            }
            Assert.Null(ModCatalogueAssetBuilder.Build(mod, "url")["preview_thumb_b64"]);
        }
        finally { Directory.Delete(root, true); }
    }
}
