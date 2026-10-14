using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>Shared ccp-mod/v1 metadata envelope. The archive stays on the creator's MEGA.</summary>
public static class ModCatalogueAsset
{
    public const string Schema = "ccp-mod/v1";

    // Thumbnail encoding is a head concern; both heads enforce the same size cap before calling.
    public static JObject Build(ModPackage mod, string downloadUrl, string? previewThumb = null)
    {
        var m = mod.Manifest;
        var asset = new JObject
        {
            ["mod_id"] = m.Id,
            ["name"] = m.Name,
            ["version"] = m.Version,
            ["author"] = m.Author,
            ["download"] = new JObject
            {
                ["provider"] = "mega",
                ["url"] = downloadUrl,
                ["installed_size_bytes"] = DirectorySize(mod.InstalledPath),
            },
        };
        if (!string.IsNullOrWhiteSpace(m.Description)) asset["description"] = m.Description;
        if (!string.IsNullOrWhiteSpace(m.MinAppVersion)) asset["min_app_version"] = m.MinAppVersion;
        if (previewThumb != null) asset["preview_thumb_b64"] = previewThumb;
        return asset;
    }

    private static long DirectorySize(string? path)
    {
        try { return new DirectoryInfo(path!).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }
}
