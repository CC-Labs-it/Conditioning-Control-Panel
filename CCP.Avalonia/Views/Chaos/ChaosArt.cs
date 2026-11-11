// PORTED from ConditioningControlPanel/Services/Chaos/ChaosArt.cs (7.1.5).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// Optional unlock-card art, reused from the old port lane.
    /// WPF Services/Chaos/ChaosArt.cs:29-68: cache decoded files, try the library
    /// before shipped art, and keep the glyph when no image can be decoded.
    /// </summary>
    internal static class ChaosArt
    {
        private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Test seam: the roots probed, in order. Null = the live roots.</summary>
        internal static Func<IEnumerable<string>>? RootsOverride;

        internal static void ResetForTest() { Cache.Clear(); RootsOverride = null; }

        public static Bitmap? TryLoad(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Cache.GetOrAdd(path, static p =>
            {
                try { return File.Exists(p) ? new Bitmap(p) : null; }
                catch { return null; }
            });
        }

        public static Bitmap? Resolve(string kind, string id)
        {
            if (string.IsNullOrWhiteSpace(kind) || kind.Contains("..") || Path.IsPathRooted(kind)
                || string.IsNullOrWhiteSpace(id) || id.Contains("..") || Path.IsPathRooted(id)) return null;
            foreach (var root in Roots())
            {
                var image = TryLoad(Path.Combine(root, kind, id + ".png"));
                if (image != null) return image;
            }
            return null;
        }

        /// <summary>The first existing convention path for a kind/id, or null.</summary>
        public static string? PathFor(string kind, string id) => FilePath(Path.Combine(kind, id + ".png"));

        /// <summary>First existing path for a file under <c>assets/Chaos/</c>, or null. Never leaves the folder.</summary>
        public static string? FilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || Path.IsPathRooted(fileName)) return null;
            foreach (var root in Roots())
            {
                var p = Path.Combine(root, fileName);
                if (File.Exists(p)) return p;
            }
            return null;
        }


        /// <summary>WPF UserAssetsPath/assets/Chaos first, then shipped assets/Chaos.</summary>
        private static IEnumerable<string> Roots()
        {
            if (RootsOverride != null) { foreach (var r in RootsOverride()) yield return r; yield break; }
            // WPF App.xaml.cs:192; Services/Chaos/ChaosArt.cs:64,142-147.
            yield return Path.Combine(CorePaths.UserData, "assets", "assets", "Chaos");
            yield return Path.Combine(AppContext.BaseDirectory, "assets", "Chaos");
        }
    }
}
