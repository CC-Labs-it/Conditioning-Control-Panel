using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class ModCreatorWindow
{
    // WPF ModCreatorWindow.Portraits.cs:508,545,570. Preserve the source JSON
    // so fields the editor cannot represent survive loading and exporting a mod.
    private string? _portraitManifestJson, _portraitBaseDir;

    private void LoadPortraitsFrom(string resourcesDir)
    {
        ClearPortraitsSection();
        var dir = Path.Combine(resourcesDir, "sounds", "companion_audio");
        var path = Path.Combine(dir, "avatar_manifest.json");
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var manifest = JObject.Parse(json);
            if (manifest.GetValue("skins", StringComparison.OrdinalIgnoreCase) is not JArray skins || manifest.GetValue("emotions", StringComparison.OrdinalIgnoreCase) is not JObject emotions || skins.Count == 0 || !emotions.HasValues) return;
            _portraitManifestJson = json;
            _portraitBaseDir = dir;
        }
        // WPF 7.4.1 ModCreatorWindow.Portraits.cs:564 catches every failure here, so an
        // unreadable or malformed manifest skips the portraits and never fails the mod load.
        catch (Exception) { }
    }

    private void WritePortraitsTo(string resourcesDir)
    {
        if (_portraitManifestJson == null || _portraitBaseDir == null) return;
        var target = Path.Combine(resourcesDir, "sounds", "companion_audio");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "avatar_manifest.json"), _portraitManifestJson);
        foreach (var skin in (JArray)JObject.Parse(_portraitManifestJson).GetValue("skins", StringComparison.OrdinalIgnoreCase)!)
        {
            var relative = (string?)(skin as JObject)?.GetValue("dir", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) continue;
            var source = Path.GetFullPath(Path.Combine(_portraitBaseDir, relative));
            var destination = Path.GetFullPath(Path.Combine(target, relative));
            if (!IsPortraitPathWithin(source, _portraitBaseDir) || !IsPortraitPathWithin(destination, target)) continue;
            if (Directory.Exists(source)) CopyPortraitDirectory(source, destination);
        }
    }

    private static bool IsPortraitPathWithin(string path, string baseDir)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir));
        return path.Equals(root, StringComparison.Ordinal)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void CopyPortraitDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) CopyPortraitDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private void ClearPortraitsSection() { _portraitManifestJson = null; _portraitBaseDir = null; }
}
