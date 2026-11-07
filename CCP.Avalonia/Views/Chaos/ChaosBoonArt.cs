using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosArt.cs:28-68,142-147: decoded art shared by full path, user assets first,
// then bundled art. Missing or unreadable art leaves the existing glyph fallback.
internal static class ChaosBoonArt
{
    private static readonly Dictionary<string, Bitmap?> Cache = new();

    internal static object? Resolve(string kind, string id) => ResolveFrom(kind, id,
        Path.Combine(CorePaths.UserData, "assets"), AppContext.BaseDirectory);

    internal static Bitmap? ResolveFrom(string kind, string id, params string[] roots)
    {
        foreach (var root in roots)
        {
            var path = Path.Combine(root, "assets", "Chaos", kind, id + ".png");
            if (!Cache.TryGetValue(path, out var bitmap))
            {
                try { bitmap = File.Exists(path) ? new Bitmap(path) : null; }
                catch (Exception) { bitmap = null; }
                Cache[path] = bitmap;
            }
            if (bitmap != null) return bitmap;
        }
        return null;
    }
}
