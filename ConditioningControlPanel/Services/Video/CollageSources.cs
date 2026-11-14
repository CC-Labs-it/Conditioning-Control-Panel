using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Where BOTH collage wallpapers (Static and Dynamic) get their images: only the images the user has
    /// SELECTED (ticked in the Assets tab) under the CCP assets <c>images</c> folder - the same filter
    /// flashes use (disabled files + disabled folders, case/separator-agnostic). Content packs and any
    /// other folder are never read. Then one theme folder per collage, so a wallpaper reads as a single
    /// theme. One rule in one place, so the two modes can never drift apart.
    /// </summary>
    internal static class CollageSources
    {
        /// <summary>What a compose needs from settings, copied on the calling (UI) thread so the
        /// background compose never reads the live, mutable selection sets.</summary>
        internal sealed class Selection
        {
            public string AssetsRoot = "", ImagesRoot = "";
            public HashSet<string> Disabled = new(StringComparer.OrdinalIgnoreCase);
            public string[] DisabledFolders = Array.Empty<string>();
        }

        /// <summary>Snapshot the current selection. Call on the UI thread.</summary>
        internal static Selection Snapshot()
        {
            var s = App.Settings?.Current;
            return new Selection
            {
                AssetsRoot = App.EffectiveAssetsPath,
                ImagesRoot = Path.Combine(App.EffectiveAssetsPath, "images"),
                Disabled = new HashSet<string>((s?.DisabledAssetPaths ?? new HashSet<string>()).Select(p => p.Replace('\\', '/')),
                    StringComparer.OrdinalIgnoreCase),
                DisabledFolders = s?.DisabledAssetFolders.ToArray() ?? Array.Empty<string>(),
            };
        }

        /// <summary>Every SELECTED image under the assets images folder, in formats the collage can
        /// decode. Hidden working folders (".temp" etc.) are skipped; hidden/system FILES count, exactly
        /// like the flash scan.</summary>
        internal static List<string> ActiveImages(Selection sel) => EnumerateActive(sel).ToList();

        /// <summary>Enough for a collage? Stops at 2, so a huge library costs nothing.</summary>
        internal static bool HasEnough(Selection sel) => EnumerateActive(sel).Take(2).Count() >= 2;

        private static IEnumerable<string> EnumerateActive(Selection sel)
        {
            if (!Directory.Exists(sel.ImagesRoot)) yield break;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            foreach (var f in Directory.EnumerateFiles(sel.ImagesRoot, "*", options))
            {
                if (!CollageComposer.SourceExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                var underImages = Path.GetRelativePath(sel.ImagesRoot, f);
                if (underImages.Split('\\', '/').Any(seg => seg.StartsWith("."))) continue;
                var rel = Path.GetRelativePath(sel.AssetsRoot, f).Replace('\\', '/');
                if (sel.Disabled.Contains(rel) || AssetFolderExclusion.IsUnderAny(rel, sel.DisabledFolders)) continue;
                yield return f;
            }
        }

        /// <summary>One random theme subfolder with at least 2 selected images (loose images in the root
        /// count as one), avoiding an immediate repeat of <paramref name="lastPack"/>. No such folder →
        /// all the selected images.</summary>
        internal static List<string> PickPack(List<string> active, string imagesRoot, ref string? lastPack, Random rng)
        {
            var groups = active
                .GroupBy(f => TopFolder(imagesRoot, f), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2)
                .ToList();
            if (groups.Count == 0) return active;

            var last = lastPack;
            var pool = (groups.Count > 1 && last != null)
                ? groups.Where(g => !string.Equals(g.Key, last, StringComparison.OrdinalIgnoreCase)).ToList()
                : groups;
            if (pool.Count == 0) pool = groups;
            var pick = pool[rng.Next(pool.Count)];
            lastPack = pick.Key;
            return pick.ToList();
        }

        private static string TopFolder(string imagesRoot, string file)
        {
            var rel = Path.GetRelativePath(imagesRoot, file);
            int i = rel.IndexOfAny(new[] { '\\', '/' });
            return i < 0 ? "" : rel.Substring(0, i);
        }
    }
}
