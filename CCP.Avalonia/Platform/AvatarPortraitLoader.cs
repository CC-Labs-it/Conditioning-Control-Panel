using System;
using System.IO;
using System.Collections.Generic;
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
            return set;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Portrait manifest could not be loaded"); return null; }
    }

    private string? Resolve(AvatarSkin skin, string file)
    {
        var path = Path.Combine(_directory, (skin.Dir ?? "").Replace('/', Path.DirectorySeparatorChar), file);
        return File.Exists(path) ? path : ContentLocator.Mirror(path);
    }

    // WPF AvatarPortraitSet.GetBucket: same emotion in skin zero, then idle.
    internal string[] Bucket(int skinIndex, string emotion)
    {
        try
        {
            skinIndex = Math.Clamp(skinIndex, 0, Manifest.Skins.Count - 1);
            string[] Load(int index, string name) => Manifest.Emotions.TryGetValue(name, out var e)
                ? e.Portraits.Where(p => !string.IsNullOrEmpty(p.File))
                    .Select(p => Resolve(Manifest.Skins[index], p.File)).Where(p => p != null).Cast<string>().ToArray()
                : Array.Empty<string>();
            var files = Load(skinIndex, emotion);
            if (files.Length == 0 && skinIndex != 0) files = Load(0, emotion);
            if (files.Length == 0 && !string.Equals(emotion, Manifest.IdleEmotion, StringComparison.OrdinalIgnoreCase))
                return Bucket(skinIndex, Manifest.IdleEmotion);
            return files;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Portrait bucket could not be loaded"); return Array.Empty<string>(); }
    }

    internal Bitmap? Open(string path)
    {
        try { return new Bitmap(path); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Portrait image could not be loaded"); return null; }
    }

    internal string? EmotionForLine(string? id) => id != null && Manifest.Lines.TryGetValue(id, out var line) ? line.Emotion : null;

    // WPF AvatarPortraitLoader.cs:245-283: first recognised mood token wins.
    internal static string EmotionForMood(string mood)
    {
        foreach (var token in mood.Split(','))
            if (MoodMap.TryGetValue(token.Trim(), out var emotion)) return emotion;
        return "neutral";
    }
    private static readonly Dictionary<string, string> MoodMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neutral"] = "neutral", ["happy"] = "happy", ["excited"] = "excited", ["praise"] = "praise",
        ["affectionate"] = "affectionate", ["teasing"] = "teasing", ["giggly"] = "giggly", ["wink"] = "wink",
        ["alluring"] = "alluring", ["inviting"] = "inviting", ["entrancing"] = "entrancing", ["dreamy"] = "dreamy",
        ["shy"] = "shy", ["tender"] = "tender", ["adoring"] = "adoring", ["surprised"] = "surprised",
        ["curious"] = "curious", ["greeting"] = "greeting", ["pleased"] = "pleased", ["overwhelmed"] = "overwhelmed",
        ["soft"] = "tender", ["possessive"] = "adoring", ["knowing"] = "teasing", ["warm"] = "affectionate",
        ["playful"] = "giggly", ["vain"] = "wink", ["amused"] = "giggly", ["indulgent"] = "pleased",
        ["patient"] = "tender", ["intimate"] = "alluring", ["deepening"] = "entrancing", ["affirming"] = "praise",
        ["smug"] = "wink", ["fond"] = "adoring", ["reverent"] = "adoring", ["afterglow"] = "dreamy",
        ["seductive"] = "alluring", ["proud"] = "praise", ["dominant"] = "adoring",
    };
}
