using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace ConditioningControlPanel.Services;

/// <summary>WPF ccp-mod/v1 metadata envelope; the binary remains hosted by its creator.</summary>
public static class ModCatalogueAssetBuilder
{
    public const string Schema = "ccp-mod/v1";
    public static JObject Build(ModPackage mod, string downloadUrl)
    {
        var m = mod.Manifest;
        var asset = new JObject
        {
            ["mod_id"] = m.Id, ["name"] = m.Name, ["version"] = m.Version, ["author"] = m.Author,
            ["download"] = new JObject { ["provider"] = "mega", ["url"] = downloadUrl,
                ["installed_size_bytes"] = SafeDirectorySize(mod.InstalledPath!) },
        };
        if (!string.IsNullOrWhiteSpace(m.Description)) asset["description"] = m.Description;
        if (!string.IsNullOrWhiteSpace(m.MinAppVersion)) asset["min_app_version"] = m.MinAppVersion;
        var thumb = TryBuildPreviewThumb(mod);
        if (thumb != null) asset["preview_thumb_b64"] = thumb;
        return asset;
    }

    private static long SafeDirectorySize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
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
