using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion.Rain;

internal sealed record RainClipInfo(string File, int Frames, int Fps);

/// <summary>Read-only part of the WPF pack store; unreadable packs are never selected.</summary>
internal sealed record RainPackReader(string Folder, IReadOnlyDictionary<string, RainClipInfo> Clips)
{
    public static bool IsValidClip(string name) => name.Length is >= 1 and <= 40 && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    public static RainPackReader? TryRead(string root, string? hash)
    {
        if (hash == null || hash.Length != 64 || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return null;
        var folder = Path.Combine(root, hash);
        try
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            if ((int?)json["rev"] != 1 || (string?)json["lookHash"] != hash || json["clips"] is not JObject entries || !entries.HasValues) return null;
            var clips = new Dictionary<string, RainClipInfo>(StringComparer.Ordinal);
            foreach (var entry in entries.Properties())
            {
                if (!IsValidClip(entry.Name) || entry.Value is not JObject value) return null;
                var file = (string?)value["file"] ?? "";
                var frames = (int?)value["frames"] ?? 0;
                var fps = (int?)value["fps"] ?? 0;
                if (frames <= 0 || fps <= 0 || (int?)value["w"] <= 0 || (int?)value["h"] <= 0 || value["w"] == null || value["h"] == null
                    || file.Length == 0 || file.Contains(':') || file.IndexOfAny(new[] { '/', '\\' }) >= 0 || !File.Exists(Path.Combine(folder, file))) return null;
                clips.Add(entry.Name, new RainClipInfo(file, frames, fps));
            }
            if (!File.Exists(Path.Combine(folder, "emotes.json")) || !File.Exists(Path.Combine(folder, "clip_timing.json"))) return null;
            try { Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new RainPackReader(folder, clips);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidCastException or ArgumentException or OverflowException)
        { return null; }
    }
}
