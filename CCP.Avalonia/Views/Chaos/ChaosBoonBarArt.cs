using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

internal static class ChaosBoonBarArt
{
    // WPF ChaosArt.Resolve: user assets first, then the shipped assets.
    internal static Bitmap? Resolve(string kind, string id) => Resolve(kind, id,
        Path.Combine(CorePaths.UserData, "assets"), AppContext.BaseDirectory);

    internal static Bitmap? Resolve(string kind, string id, string userRoot, string appRoot)
    {
        foreach (var root in new[] { userRoot, appRoot })
        {
            try
            {
                var path = Path.Combine(root, "assets", "Chaos", kind, id + ".png");
                if (File.Exists(path)) return new Bitmap(path);
            }
            catch (Exception) { } // Missing or unreadable art uses the glyph.
        }
        return null;
    }
}
