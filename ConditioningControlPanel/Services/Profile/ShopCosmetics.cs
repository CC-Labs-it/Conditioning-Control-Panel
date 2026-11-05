using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>One Prize Counter profile item: a frame or an avatar, sold as grant <c>cos.&lt;id&gt;</c>.</summary>
    public sealed record ShopCosmeticItem(string Id, string Type, string File, string Grant, bool NeedsPrime)
    {
        public bool IsFrame => Type == ShopCosmetics.FrameType;
        public bool IsAvatar => Type == ShopCosmetics.AvatarType;

        /// <summary>Loc key of the item's shop name (<c>shop_item_&lt;id with dots and dashes as _&gt;_name</c>).</summary>
        public string NameKey => "shop_item_" + Id.Replace('.', '_').Replace('-', '_') + "_name";
    }

    /// <summary>
    /// The Prize Counter's profile family (handoff-1029/B3-SHOP-DECISIONS.md section 2): four frames, two
    /// avatars and the Prime room-host frame, Astra's 512 px PNGs in <c>Resources/cosmetics/shop/</c>, listed
    /// by <c>Resources/cosmetics/shop-registry.json</c> in the cosmetics registry format (Content, off disk,
    /// like the wardrobe). Kept apart from <c>registry.json</c> on purpose: that file is the wardrobe's
    /// deco/charm pool and every id in it is a slot <see cref="WardrobeCatalog"/> validates.
    ///
    /// <para><b>Ownership.</b> A shop id is wearable only when the account holds its grant (contract 2:
    /// prizes block of /v2/user/sync). Prime items stay wearable after Prime ends; only buying needs it.
    /// Unknown grants (no snapshot yet) never strip a loadout: <see cref="KnownGrants"/> answers null and
    /// Sanitize passes the id through, the server clamps what others see.</para>
    /// </summary>
    public static class ShopCosmetics
    {
        public const string FrameType = "frame";
        public const string AvatarType = "avatar";
        public const string GrantPrefix = "cos.";

        /// <summary>
        /// Width of a frame PNG's transparent window as a share of its 512 canvas (Astra's frames measure
        /// 0.74 to 0.85). Every surface draws a frame at avatar diameter / this, centred on the avatar.
        /// </summary>
        public const double FrameWindowRatio = 0.78;

        private const int DecodePixels = 256;
        private static readonly object Gate = new();
        private static IReadOnlyList<ShopCosmeticItem>? _items;
        private static readonly Dictionary<string, ImageSource?> ImageCache = new(StringComparer.Ordinal);

        /// <summary>Parse a shop registry body. Junk rows are skipped; junk input yields an empty list. Never throws.</summary>
        public static IReadOnlyList<ShopCosmeticItem> Parse(string? json)
        {
            var list = new List<ShopCosmeticItem>();
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
                    list.Add(new ShopCosmeticItem(id, type!, file, grant, (string?)row["needs"] == "prime"));
                }
            }
            catch { list.Clear(); }
            return list;
        }

        /// <summary>The shipped registry, read once. Empty when the file is missing.</summary>
        public static IReadOnlyList<ShopCosmeticItem> Items
        {
            get
            {
                lock (Gate)
                {
                    if (_items != null) return _items;
                    string? body = null;
                    try
                    {
                        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics", "shop-registry.json");
                        if (System.IO.File.Exists(path)) body = System.IO.File.ReadAllText(path);
                    }
                    catch (Exception ex) { App.Logger?.Debug("ShopCosmetics: registry unreadable: {E}", ex.Message); }
                    _items = Parse(body);
                    return _items;
                }
            }
        }

        public static IReadOnlyList<ShopCosmeticItem> Frames => Items.Where(i => i.IsFrame).ToList();
        public static IReadOnlyList<ShopCosmeticItem> Avatars => Items.Where(i => i.IsAvatar).ToList();

        public static ShopCosmeticItem? Find(string? id)
            => string.IsNullOrWhiteSpace(id) ? null : Items.FirstOrDefault(i => i.Id == id);

        /// <summary>Frame ids this build ships, or null when the registry is missing (Sanitize: cannot validate).</summary>
        public static ISet<string>? FrameIds()
        {
            var ids = Frames.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            return ids.Count == 0 ? null : ids;
        }

        public static ISet<string> AvatarIds() => Avatars.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        /// <summary>True when <paramref name="grants"/> holds the item's grant. Pure.</summary>
        public static bool IsOwned(ShopCosmeticItem? item, IEnumerable<string>? grants)
            => item != null && grants != null && grants.Contains(item.Grant, StringComparer.Ordinal);

        /// <summary>
        /// The signed-in account's grants, or null while none are knowable (no ownership service or no
        /// snapshot yet). Null must read as "cannot check", never as "owns nothing": a loadout clamped
        /// against an empty early-boot set would be saved back stripped.
        /// </summary>
        public static ISet<string>? KnownGrants()
        {
            try
            {
                var own = App.Ownership;
                if (own == null || own.Revision <= 0) return null;
                return own.Grants.ToHashSet(StringComparer.Ordinal);
            }
            catch { return null; }
        }

        /// <summary>The item's art at card size, or null when unknown or missing. Cached, nulls too.</summary>
        public static ImageSource? GetImage(string? id)
        {
            var item = Find(id);
            if (item == null) return null;
            lock (Gate)
            {
                if (ImageCache.TryGetValue(item.Id, out var cached)) return cached;
                ImageSource? built = null;
                try
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics",
                        item.File.Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = DecodePixels;
                        bmp.EndInit();
                        if (bmp.CanFreeze) bmp.Freeze();
                        built = bmp;
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("ShopCosmetics: {Id} art failed: {E}", item.Id, ex.Message); }
                ImageCache[item.Id] = built;
                return built;
            }
        }
    }
}
