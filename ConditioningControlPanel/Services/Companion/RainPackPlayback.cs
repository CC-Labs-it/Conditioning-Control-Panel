using System;
using System.IO;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>
/// The tube side of baked Rain looks (RAIN-V2-PLAN 2.3, lane R8). Pure: no App, no WPF. With V2 on
/// and a non-empty <c>AppSettings.RainPackHash</c> whose pack the store can read, the tube plays that
/// pack's absolute folder in place of the embedded <c>rain_emotes</c>; a missing or corrupt pack falls
/// back to the embedded clips without a word. V1 never looks at packs. Inside a pack a clip name
/// resolves through the manifest to its <c>&lt;clip&gt;.strip.png</c>.
/// </summary>
internal static class RainPackPlayback
{
    /// <summary>The folder the tube plays. <paramref name="tryGetPackFolder"/> (RainPackStore.TryGetFolder)
    /// only runs for V2 on a mod with a v2 row and a set hash; <paramref name="v1Folder"/> only when V2
    /// does not apply.</summary>
    public static string? ResolveTubeFolder(string? generation, string? v2Folder, string? packHash,
        Func<string, string?> tryGetPackFolder, Func<string?> v1Folder)
    {
        if (AvatarGenerationRule.IsV2(generation))
            v2Folder = PackOrEmbedded(v2Folder, packHash, tryGetPackFolder);
        return AvatarGenerationRule.ResolveFolder(generation, v2Folder, v1Folder);
    }

    /// <summary>The pack folder when one is set and readable, else <paramref name="v2Folder"/> untouched.</summary>
    public static string? PackOrEmbedded(string? v2Folder, string? packHash, Func<string, string?> tryGetPackFolder)
    {
        if (string.IsNullOrEmpty(v2Folder) || string.IsNullOrWhiteSpace(packHash)) return v2Folder;
        try
        {
            var pack = tryGetPackFolder(packHash.Trim());
            return string.IsNullOrEmpty(pack) || !Path.IsPathRooted(pack) ? v2Folder : pack;
        }
        catch (Exception) { return v2Folder; }   // a broken pack never breaks the tube
    }

    /// <summary>True for a folder directly under a <c>rain-packs</c> root (a committed pack).</summary>
    public static bool IsPackFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder)) return false;
        var parent = Path.GetDirectoryName(folder.TrimEnd('/', Path.DirectorySeparatorChar));
        return parent != null
            && string.Equals(Path.GetFileName(parent), RainPackStore.FolderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The manifest of the pack at <paramref name="folder"/>, or null for any other folder or an
    /// unreadable pack.</summary>
    public static RainPackManifest? ManifestFor(string? folder)
    {
        if (!IsPackFolder(folder)) return null;
        var trimmed = folder!.TrimEnd('/', Path.DirectorySeparatorChar);
        var store = new RainPackStore(Path.GetDirectoryName(trimmed)!, () => null, _ => null);
        return store.TryReadManifest(Path.GetFileName(trimmed));
    }

    /// <summary>The strip file of <paramref name="clip"/> in a pack, or null when the manifest does not
    /// list it (the pack's folded emotes.json never asks for one it lacks).</summary>
    public static string? ClipPath(string folder, RainPackManifest? manifest, string clip)
        => manifest != null && manifest.Clips.TryGetValue(clip, out var c) ? Path.Combine(folder, c.File) : null;

    /// <summary>The manifest entry whose strip is <paramref name="path"/> (matched by file name).</summary>
    public static RainPackClip? ClipForFile(RainPackManifest? manifest, string? path)
    {
        if (manifest == null || string.IsNullOrEmpty(path)) return null;
        var name = Path.GetFileName(path);
        foreach (var c in manifest.Clips.Values)
            if (string.Equals(c.File, name, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    public static bool IsStripFile(Uri? uri)
        => uri != null && uri.IsFile && uri.LocalPath.EndsWith(RainStripSuffix, StringComparison.OrdinalIgnoreCase);

    public const string RainStripSuffix = ".strip.png";
}
