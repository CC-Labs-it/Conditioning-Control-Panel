using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Mods;

// Prepared set validation from WPF ModCreatorWindow.Emotes.cs:265-485.
// The caller displays the issues and must exclude sets with errors from export.
internal sealed class EmoteSetValidation
{
    internal const long SoftLimitBytes = 50L * 1024 * 1024;
    internal readonly List<(string Key, string? Arg)> Errors = new();
    internal readonly List<(string Key, string? Arg)> Warnings = new();
    internal int GifCount;
    internal long TotalBytes;
    internal bool IsValid => Errors.Count == 0;

    internal static EmoteSetValidation Inspect(string? folder)
    {
        var result = new EmoteSetValidation();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            result.Errors.Add(("mod_emote_folder_missing", null));
            return result;
        }
        try
        {
            var gifs = new HashSet<string>(Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetExtension(f).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileNameWithoutExtension(f) ?? ""), StringComparer.OrdinalIgnoreCase);
            result.GifCount = gifs.Count;
            result.TotalBytes = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            var path = Path.Combine(folder, "emotes.json");
            if (!File.Exists(path)) result.Errors.Add(("mod_emote_map_missing", null));
            else
            {
                JObject? map = null;
                try { map = JObject.Parse(File.ReadAllText(path)); }
                catch (Exception ex) { result.Errors.Add(("mod_emote_map_invalid", ex.Message)); }
                if (map != null)
                {
                    var clips = ReferencedClips(map, out var hasTalk);
                    var missing = clips.Where(c => !gifs.Contains(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
                    if (missing.Count != 0) result.Errors.Add(("mod_emote_clips_missing", string.Join(", ", missing.Select(c => c + ".gif"))));
                    if (!hasTalk) { clips.Add("talkA"); clips.Add("talkC"); }
                    var unused = gifs.Where(c => !clips.Contains(c)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
                    if (unused.Count != 0) result.Warnings.Add(("mod_emote_unused", string.Join(", ", unused)));
                    if (!hasTalk && !gifs.Contains("talkA")) result.Warnings.Add(("mod_emote_no_talk", null));
                }
            }
            if (!File.Exists(Path.Combine(folder, "clip_timing.json"))) result.Warnings.Add(("mod_emote_no_timing", null));
            if (result.TotalBytes > SoftLimitBytes) result.Warnings.Add(("mod_emote_large", null));
        }
        catch (Exception ex) { result.Errors.Add(("mod_emote_read_error", ex.Message)); }
        return result;
    }

    private static HashSet<string> ReferencedClips(JObject map, out bool hasTalk)
    {
        var clips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var idle = map["idleRotation"] as JArray;
        if (idle != null)
            foreach (var item in idle)
            {
                var clip = (string?)item["clip"];
                if (!string.IsNullOrEmpty(clip)) clips.Add(clip);
            }
        if (clips.Count == 0) clips.Add("idle");
        var talkClips = new List<string>();
        if (map["talking"] is JObject talk)
        {
            if (talk["pool"] != null) talkClips.AddRange(ReadClips(talk["pool"]));
            else foreach (var key in new[] { "short", "medium", "long" }) talkClips.AddRange(ReadClips(talk[key]));
        }
        hasTalk = talkClips.Count > 0;
        clips.UnionWith(talkClips);
        foreach (var key in new[] { "stemPrefix", "mood" })
            if (map[key] is JObject obj)
                foreach (var p in obj.Properties())
                {
                    if (p.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    var value = (string?)p.Value;
                    if (!string.IsNullOrEmpty(value)) clips.Add(value);
                }
        if (map["clickEmotes"] is JArray click) clips.UnionWith(ReadClips(click));
        if (map["expressive"] is JObject expressive) clips.UnionWith(ReadClips(expressive["pool"]));
        return clips;
    }

    private static IEnumerable<string> ReadClips(JToken? token)
    {
        if (token == null) return Array.Empty<string>();
        if (token is JArray array) return array.Select(x => (string?)x).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToArray();
        var value = (string?)token;
        return string.IsNullOrEmpty(value) ? Array.Empty<string>() : new[] { value };
    }
}
