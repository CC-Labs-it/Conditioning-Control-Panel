using System;
using System.IO;

namespace ConditioningControlPanel.Services;

/// <summary>WPF empty-library probe: recurse, ignore case, and do not nag on IO failure.</summary>
public static class NotificationLibrary
{
    public static bool HasMedia(string folder, bool wallpaper)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" || (!wallpaper && ext is ".gif" or ".webp")) return true;
            }
            return false;
        }
        catch { return true; }
    }
}
