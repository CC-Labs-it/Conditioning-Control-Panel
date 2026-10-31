using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services
{
    /// <summary>One Prize Counter profile item: a frame or an avatar, sold as grant <c>cos.&lt;id&gt;</c>.</summary>
    public sealed record ShopProfileItem(string Id, string Type, string File, string Grant, bool NeedsPrime)
    {
        public bool IsFrame => Type == ShopCosmeticsRegistry.FrameType;
        public bool IsAvatar => Type == ShopCosmeticsRegistry.AvatarType;

        /// <summary>Loc key of the item's shop name (<c>shop_item_&lt;id with dots and dashes as _&gt;_name</c>).</summary>
        public string NameKey => "shop_item_" + Id.Replace('.', '_').Replace('-', '_') + "_name";
    }

    // Parser and item rules copied from WPF Services/Profile/ShopCosmetics.cs.
    // A separate type keeps the WPF image cache and ownership service unchanged.
    public static class ShopCosmeticsRegistry
    {
        public const string FrameType = "frame", AvatarType = "avatar", GrantPrefix = "cos.";
        public const double FrameWindowRatio = 0.78;
        /// <summary>Parse a shop registry body. Junk rows are skipped; junk input yields an empty list. Never throws.</summary>
        public static IReadOnlyList<ShopProfileItem> Parse(string? json)
        {
            var list = new List<ShopProfileItem>();
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return list;
                if (JObject.Parse(json)["items"] is not JArray rows) return list;
                foreach (var row in rows.OfType<JObject>())
                {
                    var id = (string?)row["id"];
                    var type = (string?)row["type"];
                    var file = (string?)row["file"];
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(file)) continue;
                    if (type != FrameType && type != AvatarType) continue;
                    if (file.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(file)) continue;
                    if (list.Any(i => i.Id == id)) continue;
                    var grant = (string?)row["unlock"];
                    if (string.IsNullOrWhiteSpace(grant) || !grant.StartsWith(GrantPrefix, StringComparison.Ordinal))
                        grant = GrantPrefix + id;
                    list.Add(new ShopProfileItem(id, type!, file, grant, (string?)row["needs"] == "prime"));
                }
            }
            catch { list.Clear(); }
            return list;
        }

        public static readonly IReadOnlyList<ShopProfileItem> Items = Load();
        private static IReadOnlyList<ShopProfileItem> Load()
        {
            try { return Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics", "shop-registry.json"))); }
            catch { return Array.Empty<ShopProfileItem>(); }
        }
        public static IEnumerable<ShopProfileItem> Frames => Items.Where(i => i.IsFrame);
        public static IEnumerable<ShopProfileItem> Avatars => Items.Where(i => i.IsAvatar);
        public static ShopProfileItem? Find(string? id) => Items.FirstOrDefault(i => i.Id == id);
        public static ISet<string> FrameIds() => Frames.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        public static ISet<string> AllAvatarIds() => CosmeticsPool.AvatarIds.Concat(Avatars.Select(i => i.Id)).ToHashSet(StringComparer.Ordinal);
        public static IReadOnlyDictionary<string, string> Grants() => Items.ToDictionary(i => i.Id, i => i.Grant, StringComparer.Ordinal);
        public static bool IsOwned(ShopProfileItem? item, IEnumerable<string>? grants)
            => item != null && grants != null && grants.Contains(item.Grant, StringComparer.Ordinal);
    }
}
