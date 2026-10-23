// Adapted from WPF DtrhModContent.cs at b8a1ffe9 (race init + media merge); root-relative URLs use WebAssetServer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class DtrhModContent
{
    private static readonly string[] AudioExts = { ".mp3", ".ogg", ".wav" };
    private static readonly string[] PoolIds = { "drift", "sensory", "descent", "babble", "resolve" };
    private const int MaxClipsPerPool = 200;
    private const long MaxPortraitBytes = 10L * 1024 * 1024;
    private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
    private const long MaxImageBytes = 50L * 1024 * 1024;
    private const long MaxVideoBytes = 500L * 1024 * 1024;

    public sealed class DtrhModManifest
    {
        [JsonProperty("tintA")] public string? TintA { get; set; }
        [JsonProperty("tintB")] public string? TintB { get; set; }
        [JsonProperty("imageMode")] public string? ImageMode { get; set; }   // mix | replace (default mix)
        [JsonProperty("driftMode")] public string? DriftMode { get; set; }   // replace | mix (default replace)
        [JsonProperty("drone")] public string? Drone { get; set; }           // relative path under dtrh/
    }

    public static string? ModDtrhRoot()
    {
        try
        {
            var installed = CoreMods.ActiveModPackage?.InstalledPath;
            if (string.IsNullOrEmpty(installed)) return null;
            var root = Path.Combine(installed, "resources", "dtrh");
            return Directory.Exists(root) ? root : null;
        }
        catch { return null; }
    }

    public static object? BuildInitPayload()
    {
        try
        {
            var root = ModDtrhRoot();
            if (root == null) return null;
            var mod = CoreMods.ActiveModPackage!;
            var cfg = LoadManifest(root);

            var pools = CollectDriftPools(root);
            var portrait = CollectPortrait(root);
            var droneUrl = ResolveDrone(root, cfg.Drone);
            var tint = ParseTintPair(cfg.TintA, cfg.TintB);

            bool hasAnything = pools.Values.Any(p => p.Count > 0) || portrait != null || droneUrl != null || tint != null;
            if (!hasAnything && !HasAnyMedia(root)) return null;

            Log.Information(
                "DtrhModContent: mod '{Id}' ships dtrh content ({Clips} clips, portrait={P}, drone={D}, tint={T})",
                mod.Id, pools.Values.Sum(p => p.Count), portrait != null, droneUrl != null, tint != null);

            return new
            {
                id = mod.Id,
                companion = (mod.Manifest.Identity?.CompanionName ?? mod.Name ?? "").ToUpperInvariant(),
                tint,
                droneUrl,
                drift = new
                {
                    mode = NormalizeMode(cfg.DriftMode, defaultMode: "replace"),
                    pools = pools.ToDictionary(kv => kv.Key, kv => kv.Value.Select(f => new
                    {
                        file = Path.GetFileName(f),
                        url = ToModUrl(root, f),
                    })),
                },
                portrait,
            };
        }
        catch (Exception ex)
        {
            Log.Warning("DtrhModContent.BuildInitPayload failed: {E}", ex.Message);
            return null;
        }
    }

    public static void MergeMedia(GameMediaManifest.Manifest m)
    {
        try
        {
            var root = ModDtrhRoot();
            if (root == null) return;
            var cfg = LoadManifest(root);
            bool replace = NormalizeMode(cfg.ImageMode, defaultMode: "mix") == "replace";

            var images = CollectMedia(Path.Combine(root, "images"), root, ImageExts, MaxImageBytes);
            var videos = CollectMedia(Path.Combine(root, "videos"), root, VideoExts, MaxVideoBytes);
            if (images.Count == 0 && videos.Count == 0) return;

            if (replace && images.Count > 0) m.Images.Clear();
            if (replace && videos.Count > 0) m.Videos.Clear();
            m.Images.AddRange(images);
            m.Videos.AddRange(videos);
            Log.Information("DtrhModContent: merged {I} mod images, {V} mod videos ({Mode})",
                images.Count, videos.Count, replace ? "replace" : "mix");
        }
        catch (Exception ex)
        {
            Log.Warning("DtrhModContent.MergeMedia failed: {E}", ex.Message);
        }
    }


    private static string NormalizeMode(string? mode, string defaultMode)
    {
        var m = mode?.Trim().ToLowerInvariant();
        return m is "mix" or "replace" ? m : defaultMode;
    }

    private static DtrhModManifest LoadManifest(string root)
    {
        try
        {
            var path = Path.Combine(root, "dtrh.json");
            if (!File.Exists(path)) return new DtrhModManifest();
            var parsed = JsonConvert.DeserializeObject<DtrhModManifest>(File.ReadAllText(path));
            if (parsed == null)
            {
                Log.Warning("DtrhModContent: dtrh.json parsed to null - using defaults");
                return new DtrhModManifest();
            }
            return parsed;
        }
        catch (Exception ex)
        {
            Log.Warning("DtrhModContent: dtrh.json unreadable ({E}) - using defaults", ex.Message);
            return new DtrhModManifest();
        }
    }

    private static Dictionary<string, List<string>> CollectDriftPools(string root)
    {
        var pools = PoolIds.ToDictionary(id => id, _ => new List<string>());
        var dir = Path.Combine(root, "barks");
        if (!Directory.Exists(dir)) return pools;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); } catch { return pools; }
        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!AudioExts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            var pool = PoolFor(Path.GetFileNameWithoutExtension(f));
            if (pools[pool].Count >= MaxClipsPerPool) continue;
            pools[pool].Add(f);
        }
        return pools;
    }

    private static string PoolFor(string stem)
    {
        foreach (var id in PoolIds)
            if (stem.StartsWith("fall_" + id + "_", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("fall_" + id, StringComparison.OrdinalIgnoreCase))
                return id;
        return "drift";
    }

    private static object? CollectPortrait(string root)
    {
        try
        {
            var dir = Path.Combine(root, "portrait");
            if (!Directory.Exists(dir)) return null;
            string? defaultUrl = null;
            var emotes = new Dictionary<string, string>();
            foreach (var f in Directory.EnumerateFiles(dir, "*.png"))
            {
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }
                if (len <= 0 || len > MaxPortraitBytes) continue;
                var stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                var url = ToModUrl(root, f);
                if (stem == "default") defaultUrl = url;
                else emotes[stem] = url;
            }
            if (defaultUrl == null) return null;
            return new { defaultUrl, emotes };
        }
        catch { return null; }
    }

    private static string? ResolveDrone(string root, string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return null;
        try
        {
            if (rel.Contains("..") || Path.IsPathRooted(rel))
            {
                Log.Warning("DtrhModContent: rejected drone path '{P}'", rel);
                return null;
            }
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return null;
            if (!AudioExts.Contains(Path.GetExtension(full).ToLowerInvariant())) return null;
            if (!File.Exists(full)) return null;
            return ToModUrl(root, full);
        }
        catch { return null; }
    }

    private static object? ParseTintPair(string? a, string? b)
    {
        var ca = ParseHexRgb(a);
        var cb = ParseHexRgb(b) ?? ca;   // one color is enough - reuse it for both stops
        if (ca == null) return null;
        return new { a = ca, b = cb };
    }

    private static string? ParseHexRgb(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim();
        if (hex.Length != 7 || hex[0] != '#') return null;
        try
        {
            int r = Convert.ToInt32(hex.Substring(1, 2), 16);
            int g = Convert.ToInt32(hex.Substring(3, 2), 16);
            int bl = Convert.ToInt32(hex.Substring(5, 2), 16);
            return $"{r},{g},{bl}";
        }
        catch { return null; }
    }

    private static bool HasAnyMedia(string root)
    {
        try
        {
            foreach (var sub in new[] { "images", "videos" })
            {
                var dir = Path.Combine(root, sub);
                if (Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any()) return true;
            }
        }
        catch { }
        return false;
    }

    private static List<GameMediaManifest.Entry> CollectMedia(string dir, string root, string[] exts, long cap)
    {
        var list = new List<GameMediaManifest.Entry>();
        if (!Directory.Exists(dir)) return list;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); } catch { return list; }
        foreach (var f in files)
        {
            if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            long len;
            try { len = new FileInfo(f).Length; } catch { continue; }
            if (len <= 0 || len > cap) continue;
            list.Add(new GameMediaManifest.Entry("mod:" + Path.GetFileName(f), ToModUrl(root, f)));
        }
        return list;
    }

    private static string ToModUrl(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return "/ccp.mod/" + escaped;
    }
}
