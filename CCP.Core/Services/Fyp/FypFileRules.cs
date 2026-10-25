using System;
using System.IO;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>WPF FypFileMenu library-id policy, shared by both heads.</summary>
internal static class FypFileRules
{
    // Same list as FypAssetManifest.VideoExts.
    private static readonly string[] ServedVideoExts = { ".mp4", ".webm", ".m4v" };

    /// <summary>
    /// Full path for a library id, or null for an online id, an empty id, a path that escapes
    /// <paramref name="root"/>, or one carrying a quote (it could not be put on a command line
    /// safely, and a real Windows path never holds one).
    /// </summary>
    public static string? ResolveLocal(string? root, string? id)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(id)) return null;
        if (id.StartsWith("scrolller/", StringComparison.Ordinal)) return null;
        if (id.IndexOf('"') >= 0 || id.IndexOf(':') >= 0) return null;
        // Only the shape the manifest itself serves (FypAssetManifest.Collect): a clip under
        // videos/ or a gif under images/, never a dot folder (the remote cache lives in .temp),
        // never any other extension. Process.Start opens with the shell, so an .exe or .lnk that
        // happens to sit in the assets folder must not be reachable from the page.
        var normalized = id.Replace('\\', '/');
        var parts = normalized.Split('/');
        if (parts.Length < 2) return null;
        bool isVideo = string.Equals(parts[0], "videos", StringComparison.OrdinalIgnoreCase);
        bool isGif = string.Equals(parts[0], "images", StringComparison.OrdinalIgnoreCase);
        if (!isVideo && !isGif) return null;
        foreach (var p in parts)
            if (p.Length == 0 || p.StartsWith('.')) return null;
        var ext = Path.GetExtension(normalized).ToLowerInvariant();
        if (isVideo ? Array.IndexOf(ServedVideoExts, ext) < 0 : ext != ".gif") return null;
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) return null;
            return full;
        }
        catch
        {
            return null;
        }
    }

}
