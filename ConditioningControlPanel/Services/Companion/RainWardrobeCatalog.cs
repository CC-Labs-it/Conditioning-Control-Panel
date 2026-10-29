using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// Which of Rain's wardrobe items the Prize Counter sells, and so which ones a look may only wear
    /// once owned (handoff-1029/B3-SHOP-DECISIONS.md section 2: one grant <c>cos.&lt;id&gt;</c> per item,
    /// single items only, never outfit sets). Everything else on the page (Rain's authored default look,
    /// shoes, legwear, arms, ankles, skin, colours) stays free. Ownership is the account's grants
    /// (<c>App.Ownership</c>, the prizes block of /v2/user/sync). PURE.
    /// </summary>
    public static class RainWardrobeCatalog
    {
        /// <summary>One sellable item: the shop id, the page slot and the page's item id.</summary>
        public sealed record Item(string ShopId, string Slot, string WardrobeId)
        {
            public string Grant => "cos." + ShopId;
            /// <summary>The page's lock key (host-bridge.js <c>?locked=</c>).</summary>
            public string LockKey => Slot + ":" + WardrobeId;
        }

        private static Item Dress(string id) => new("rain.dress." + id, "dress", id);
        private static Item Suit(string id) => new("rain.suit." + id, "dress", id);
        private static Item Hair(string id) => new("rain.hair." + id, "hair", id);
        private static Item Acc(string id) => new("rain.acc." + id, RainLookCodec.AccessoriesSlot, id);

        /// <summary>The 7.2 shelves (server shop.js + the counter trunk) and the named later drops.</summary>
        public static readonly IReadOnlyList<Item> Items = new[]
        {
            Dress("midnight-cocktail"), Dress("rose-flare"), Dress("ivory-midi"), Dress("plum-asymmetric"),
            Dress("scarlet-bell"), Dress("teal-tulip"), Dress("black-highlow"),
            Suit("hazmat-yellow"), Suit("action-yellow"),
            Hair("chestnut-bob"), Hair("copper-lob"), Hair("ink-sleek"), Hair("rose-waves"),
            Hair("platinum-long"), Hair("auburn-volume"),
            Acc("silver-drops"), Acc("cat-eye-frames"), Acc("hazmat-mask"), Acc("back-katana"),
            // Later weekly drops (B3 section 2): sold, so never free in the meantime.
            Hair("honey-roundbob"), Hair("espresso-sidesweep"), Acc("rose-gold-collar"), Acc("midnight-ribbon"),
        };

        /// <summary>The page's lock keys for every item the grants do not cover, in catalog order.</summary>
        public static IReadOnlyList<string> LockedKeys(IEnumerable<string>? grants)
        {
            var owned = new HashSet<string>(grants ?? Array.Empty<string>(), StringComparer.Ordinal);
            return Items.Where(i => !owned.Contains(i.Grant)).Select(i => i.LockKey).ToList();
        }

        /// <summary>True when (slot, id) is a sellable item the grants do not cover.</summary>
        public static bool IsLocked(string slot, string id, IEnumerable<string>? grants)
            => LockedKeys(grants).Contains(slot + ":" + id, StringComparer.Ordinal);

        /// <summary>The stored look with every item the account does not own taken off.</summary>
        public static string Clamp(string? stored, IEnumerable<string>? grants)
        {
            var locked = new HashSet<string>(LockedKeys(grants), StringComparer.Ordinal);
            return RainLookCodec.Normalize(stored, (slot, id) => locked.Contains(slot + ":" + id));
        }
    }

    /// <summary>
    /// The two big files Rain's fitting room needs (avatar.glb, about 119 MB; pelvis-data.js, about 26 MB).
    /// They never ship in this public repo: the window maps them to <c>https://rain.assets/</c> from
    /// <c>CCP_RAIN_ASSETS_DIR</c> when set, else <c>&lt;user data&gt;/rain-assets/</c>
    /// (%LOCALAPPDATA%/ConditioningControlPanel/rain-assets). Same rule as the 7.2 Avalonia head.
    /// </summary>
    public static class RainWardrobeAssets
    {
        public const string EnvVar = "CCP_RAIN_ASSETS_DIR";
        public const string FolderName = "rain-assets";
        public static readonly IReadOnlyList<string> Files = new[] { "avatar.glb", "pelvis-data.js" };

        /// <summary>The folder, pure: a non-blank <paramref name="env"/> wins, else &lt;userData&gt;/rain-assets.</summary>
        public static string ResolveDir(string? env, string userData) =>
            string.IsNullOrWhiteSpace(env) ? Path.Combine(userData, FolderName) : Path.GetFullPath(env.Trim());

        /// <summary>The files absent from <paramref name="dir"/> (empty = ready). A missing folder misses both.</summary>
        public static IReadOnlyList<string> Missing(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return Files;
            try { return Files.Where(f => !File.Exists(Path.Combine(dir, f))).ToList(); }
            catch (Exception) { return Files; }
        }
    }
}
