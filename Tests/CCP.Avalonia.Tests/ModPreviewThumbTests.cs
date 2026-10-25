using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Windows;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModPreviewThumbTests
{
    [Fact]
    public void PreviewIsAJpegAtMost160PixelsWideAnd64KiB()
    {
        var root = Directory.CreateTempSubdirectory("ccp-mod-thumb-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "resources"));
            var path = Path.Combine(root, "resources", "art.png");
            using (var bitmap = new SKBitmap(320, 160))
            {
                bitmap.Erase(SKColors.HotPink);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            var mod = new ModPackage(new ModManifest { PreviewImage = "art.png" }, root, false);
            var encoded = MainShellWindow.TryBuildModPreviewThumb(mod);
            var bytes = Convert.FromBase64String(Assert.IsType<string>(encoded));
            Assert.InRange(bytes.Length, 1, 64 * 1024);
            Assert.Equal(0xff, bytes[0]);
            Assert.Equal(0xd8, bytes[1]);
            using var decoded = SKBitmap.Decode(bytes);
            Assert.Equal(160, decoded.Width);
            Assert.Equal(80, decoded.Height);
            using (var small = new SKBitmap(80, 40))   // WPF DecodePixelWidth=160 scales small previews up
            {
                small.Erase(SKColors.HotPink);
                using var image = SKImage.FromBitmap(small);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            using (var upscaled = SKBitmap.Decode(Convert.FromBase64String(MainShellWindow.TryBuildModPreviewThumb(mod)!)))
                Assert.Equal((160, 80), (upscaled.Width, upscaled.Height));
            File.WriteAllText(path, "corrupt image");
            Assert.Null(MainShellWindow.TryBuildModPreviewThumb(mod));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingPreviewIsOptional() => Assert.Null(MainShellWindow.TryBuildModPreviewThumb(
        new ModPackage(new ModManifest { PreviewImage = "missing.png" }, "/unused", false)));
}
