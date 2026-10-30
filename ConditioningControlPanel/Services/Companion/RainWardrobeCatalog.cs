using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// Which of Rain's wardrobe items a look may wear, as the wardrobe window asks it (handoff-1029/B3-SHOP-DECISIONS.md
    /// section 2: one grant <c>cos.&lt;id&gt;</c> per item, single items only, never outfit sets). The source is
    /// <c>Resources/web/rain-wardrobe/items.json</c> through <see cref="RainItemAccess"/> (RAIN-V2-PLAN 2.5):
    /// shop rows need their grant, level / achievement rows need the account's progress, the rest is free.
    /// Grants are the account's (<c>App.Ownership</c>, the prizes block of /v2/user/sync); a null
    /// <c>progress</c> reads the live app's level and achievements.
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

        /// <summary>The shop rows of items.json (catalog v2 plus the named later drops), in manifest order.</summary>
        public static IReadOnlyList<Item> Items => RainItemManifest.Shipped.Items
            .Where(i => i.Access == RainItemAccessKind.Shop && i.Grant!.StartsWith("cos.", StringComparison.Ordinal))
            .Select(i => new Item(i.Grant!.Substring(4), i.Slot, i.PageId)).ToList();

        private static RainItemAccess Access(IEnumerable<string>? grants, IRainProgressSource? progress)
            => new(RainItemManifest.Shipped, new RainGrantSet(grants), progress ?? new AppRainProgressSource());

        /// <summary>The page's lock keys for every item the account cannot wear yet, in manifest order.</summary>
        public static IReadOnlyList<string> LockedKeys(IEnumerable<string>? grants, IRainProgressSource? progress = null)
        {
            var access = Access(grants, progress);
            return access.Manifest.Items.Where(i => !access.StateOf(i).Wearable).Select(i => i.Id).ToList();
        }

        /// <summary>True when (slot, id) is a manifest item the account cannot wear yet.</summary>
        public static bool IsLocked(string slot, string id, IEnumerable<string>? grants, IRainProgressSource? progress = null)
        {
            var state = Access(grants, progress).StateOf(slot, id);
            return state.Item != null && !state.Wearable;
        }

        /// <summary>The stored look with every piece the account cannot wear taken off (<see cref="RainLookClamp"/>).</summary>
        public static string Clamp(string? stored, IEnumerable<string>? grants, IRainProgressSource? progress = null)
            => RainLookClamp.Clamp(stored, Access(grants, progress));
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
