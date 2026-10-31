using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>Read-only profile art registry. No shop host or purchase behavior.
/// WPF spec: Services/Profile/ShopCosmetics.cs (Parse, Items, frame geometry).</summary>
public static class ShopProfileArt
{
    public const double FrameWindowRatio = 0.78;
    public sealed record Item(string Id, string Type, string File);

    public static IReadOnlyList<Item> Parse(string? json)
    {
        var list = new List<Item>();
        try
        {
            if (string.IsNullOrWhiteSpace(json) || JObject.Parse(json)["items"] is not JArray rows) return list;
            foreach (var row in rows.OfType<JObject>())
            {
                var id = (string?)row["id"];
                var type = (string?)row["type"];
                var file = (string?)row["file"];
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(file)) continue;
                if (type != "frame" && type != "avatar") continue;
                if (file.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(file)) continue;
                if (list.Any(i => i.Id == id)) continue;
                list.Add(new Item(id, type!, file));
            }
        }
        catch { list.Clear(); }
        return list;
    }

    private static readonly Lazy<IReadOnlyList<Item>> Catalog = new(() =>
    {
        try { return Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics", "shop-registry.json"))); }
        catch { return Array.Empty<Item>(); }
    });

    public static IReadOnlyList<Item> Items => Catalog.Value;
    public static string? ArtPath(string? id, string type)
    {
        var item = Items.FirstOrDefault(i => i.Id == id && i.Type == type);
        return item == null ? null : Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics",
            item.File.Replace('/', Path.DirectorySeparatorChar));
    }

    public static (ISet<string> Avatars, ISet<string>? Frames) KnownIds()
    {
        var avatars = CosmeticsPool.AvatarIds.ToHashSet(StringComparer.Ordinal);
        avatars.UnionWith(Items.Where(i => i.Type == "avatar").Select(i => i.Id));
        var frames = Items.Where(i => i.Type == "frame").Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        return (avatars, frames.Count == 0 ? null : frames);
    }
}
