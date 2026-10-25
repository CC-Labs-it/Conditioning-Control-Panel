using System;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace ConditioningControlPanel.Services;

/// <summary>WPF ccp-mod/v1 metadata envelope; the binary remains hosted by its creator.</summary>
public static class ModCatalogueAssetBuilder
{
    public const string Schema = ModCatalogueAsset.Schema;
    public static JObject Build(ModPackage mod, string downloadUrl)
    {
        return ModCatalogueAsset.Build(mod, downloadUrl, TryBuildPreviewThumb(mod));
    }

    private static string? TryBuildPreviewThumb(ModPackage mod)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mod.Manifest.PreviewImage) || mod.InstalledPath == null) return null;
            var path = Path.Combine(mod.InstalledPath, "resources", mod.Manifest.PreviewImage);
            if (!File.Exists(path)) return null;
            using var codec = SKCodec.Create(path);
            if (codec == null) return null;
            // Codec-native downsampling avoids decoding full-size JPEG artwork before resizing.
            var size = codec.GetScaledDimensions(Math.Min(1f, 160f / codec.Info.Width));
            using var decoded = SKBitmap.Decode(codec, new SKImageInfo(size.Width, size.Height));
            if (decoded == null) return null;
            var height = Math.Max(1, (int)Math.Round(codec.Info.Height * 160d / codec.Info.Width));
            using var thumb = decoded.Resize(new SKImageInfo(160, height), SKFilterQuality.Medium);
            if (thumb == null) return null;
            using var image = SKImage.FromBitmap(thumb);
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            return jpeg == null || jpeg.Size > 64 * 1024 ? null : Convert.ToBase64String(jpeg.ToArray());
        }
        catch (Exception ex)
        {
            // WPF omits unusable previews without blocking the metadata submission.
            Serilog.Log.Debug("[Catalogue] Mod preview thumb failed: {Error}", ex.Message);
            return null;
        }
    }
}
