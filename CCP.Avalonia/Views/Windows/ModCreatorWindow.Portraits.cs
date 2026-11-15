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
        var json = File.ReadAllText(path);
        try
        {
            var manifest = JObject.Parse(json);
            if (manifest["skins"] is not JArray skins || manifest["emotions"] is not JObject emotions || skins.Count == 0 || !emotions.HasValues) return;
            _portraitManifestJson = json;
            _portraitBaseDir = dir;
        }
        catch (Newtonsoft.Json.JsonException) { }
    }

    private void WritePortraitsTo(string resourcesDir)
    {
        if (_portraitManifestJson == null || _portraitBaseDir == null) return;
        var target = Path.Combine(resourcesDir, "sounds", "companion_audio");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "avatar_manifest.json"), _portraitManifestJson);
        foreach (var skin in (JArray)JObject.Parse(_portraitManifestJson)["skins"]!)
        {
            var relative = (string?)skin["dir"];
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) continue;
            var source = Path.GetFullPath(Path.Combine(_portraitBaseDir, relative));
            var destination = Path.GetFullPath(Path.Combine(target, relative));
            if (!source.StartsWith(Path.GetFullPath(_portraitBaseDir) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || !destination.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (Directory.Exists(source)) CopyPortraitDirectory(source, destination);
        }
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
