using System;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/Companion/AvatarPortraitLoader.cs:24-194: packaged manifest first,
// then the per-mod content folder. A partial download must not replace the legacy poses.
internal sealed class AvatarPortraitLoader
{
    private readonly string _directory;
    internal AvatarPortraitManifest Manifest { get; }
    private AvatarPortraitLoader(AvatarPortraitManifest manifest, string directory) => (Manifest, _directory) = (manifest, directory);

    internal static AvatarPortraitLoader? Load()
    {
        try
        {
            var installed = CoreMods.ActiveModPackage?.InstalledPath;
            var local = string.IsNullOrEmpty(installed) ? null : Path.Combine(installed, "resources", "sounds", "companion_audio", "avatar_manifest.json");
            var path = local != null && File.Exists(local) ? local : ContentLocator.Resolve(Path.Combine("Resources", "sounds", "companion_audio", "mods", CoreMods.ActiveModId ?? "", "avatar_manifest.json"));
            if (!File.Exists(path)) return null;
            var manifest = JsonConvert.DeserializeObject<AvatarPortraitManifest>(File.ReadAllText(path));
            if (manifest?.Skins == null || manifest.Skins.Count == 0 || manifest.Emotions == null || manifest.Emotions.Count == 0) return null;
            var set = new AvatarPortraitLoader(manifest, Path.GetDirectoryName(path)!);
            var files = manifest.Skins.SelectMany(s => manifest.Emotions.Values.SelectMany(e => e.Portraits.Select(p => (s, p))))
                .Where(x => !string.IsNullOrEmpty(x.p.File)).ToArray();
            if (files.Length == 0 || files.Count(x => set.Resolve(x.s, x.p.File) != null) < Math.Min(4, files.Length)) return null;
            using var idle = set.Idle(0);
            return idle == null ? null : set;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Portrait manifest could not be loaded"); return null; }
    }

    private string? Resolve(AvatarSkin skin, string file)
    {
        var path = Path.Combine(_directory, (skin.Dir ?? "").Replace('/', Path.DirectorySeparatorChar), file);
        return File.Exists(path) ? path : ContentLocator.Mirror(path);
    }

    internal Bitmap? Idle(int skinIndex)
    {
        try
        {
            if (!Manifest.Emotions.TryGetValue(Manifest.IdleEmotion, out var emotion)) return null;
            foreach (var index in new[] { Math.Clamp(skinIndex, 0, Manifest.Skins.Count - 1), 0 }.Distinct())
                foreach (var portrait in emotion.Portraits)
                    if (!string.IsNullOrEmpty(portrait.File) && Resolve(Manifest.Skins[index], portrait.File) is { } path)
                        try { return new Bitmap(path); } catch (Exception ex) { Serilog.Log.Warning(ex, "Portrait image could not be loaded"); }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Portrait idle could not be loaded"); }
        return null;
    }
}
