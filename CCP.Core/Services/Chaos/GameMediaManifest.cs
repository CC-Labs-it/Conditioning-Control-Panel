using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>
    /// The user's library as the game pages see it (WPF Services/Chaos/DtrhAssetManifest.Build, the local half):
    /// the active library's images/ and videos/, deselected files left out, oversize or undecodable media
    /// counted as skipped, sampled down to 5000 entries keeping the image:video ratio. URLs are the asset
    /// head's ccp.assets URL (WPF https://ccp.assets/&lt;rel&gt;). <see cref="Deal"/> answers a Back Room
    /// station's <c>media-request</c> from it (WPF BackRoomBridge.OnMediaRequestAsync, local source only).
    /// </summary>
    internal static class GameMediaManifest
    {
        private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
        internal const long MaxImageBytes = 50L * 1024 * 1024;
        internal const long MaxVideoBytes = 500L * 1024 * 1024;
        internal const int MaxEntries = 5000;
        private const int MaxWalkDepth = 8;

        internal sealed record Entry(string Name, string Url);

        internal sealed class Manifest
        {
            public List<Entry> Images { get; } = new();
            public List<Entry> Videos { get; } = new();
            public int Skipped { get; set; }
            public bool Truncated { get; set; }
        }

        internal static Manifest Build(string? root, Func<string, string> url, IEnumerable<string>? disabledPaths, bool videos = true)
        {
            var m = new Manifest();
            if (string.IsNullOrEmpty(root)) return m;
            try
            {
                var disabled = new HashSet<string>((disabledPaths ?? Array.Empty<string>()).Select(p => p.Replace('\\', '/')),
                    StringComparer.OrdinalIgnoreCase);
                int accepted = 0;
                foreach (var isImage in videos ? new[] { true, false } : new[] { true })
                {
                    var dir = Path.Combine(root, isImage ? "images" : "videos");
                    if (!Directory.Exists(dir)) continue;
                    var exts = isImage ? ImageExts : VideoExts;
                    var otherExts = isImage ? VideoExts : ImageExts;
                    long cap = isImage ? MaxImageBytes : MaxVideoBytes;
                    foreach (var f in Walk(dir, 0))
                    {
                        if (accepted >= MaxEntries * 2) break;
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (!exts.Contains(ext))
                        {
                            if (!otherExts.Contains(ext) && IsMediaLike(ext)) m.Skipped++;
                            continue;
                        }
                        string rel;
                        try { rel = Path.GetRelativePath(root, f).Replace('\\', '/'); }
                        catch { rel = Path.GetFileName(f); }
                        if (disabled.Count > 0 && disabled.Contains(rel)) continue;
                        long len;
                        try { len = new FileInfo(f).Length; } catch { continue; }
                        if (len <= 0 || len > cap) { m.Skipped++; continue; }
                        accepted++;
                        (isImage ? m.Images : m.Videos).Add(new Entry(Path.GetFileName(f), url(rel)));
                    }
                }
                int total = m.Images.Count + m.Videos.Count;
                if (total > MaxEntries)
                {
                    var rng = new Random();
                    int imgKeep = (int)Math.Round(MaxEntries * (double)m.Images.Count / total);
                    Downsample(m.Images, imgKeep, rng);
                    Downsample(m.Videos, MaxEntries - imgKeep, rng);
                    m.Truncated = true;
                }
                Log.Information("[Game] manifest: {I} images, {V} videos, {S} skipped{T}",
                    m.Images.Count, m.Videos.Count, m.Skipped, m.Truncated ? " (truncated)" : "");
            }
            catch (Exception ex) { Log.Warning("[Game] manifest build failed: {E}", ex.Message); }
            return m;
        }

        /// <summary>The user's pictures through a head's asset URL (the Avalonia head's WebAssetServer.AssetUrl):
        /// images/ only, the half a media deal reads (WPF BackRoomMedia deals from FlashService's image snapshot,
        /// never the videos).</summary>
        internal static Manifest BuildLocal(Func<string, string> assetUrl)
        {
            return Build(CorePaths.EffectiveAssets, assetUrl, CoreSettings.Current.DisabledAssetPaths, videos: false);
        }

        /// <summary>The active library, images AND videos (WPF DtrhAssetManifest.Build's local half).</summary>
        internal static Manifest BuildActive(Func<string, string> assetUrl) =>
            Build(CorePaths.EffectiveAssets, assetUrl, CoreSettings.Current.DisabledAssetPaths);

        /// <summary>The page's <c>manifest</c> frame (WPF DtrhHostService.PostManifest / CaucusHostService.OnPageReady),
        /// read by hostMedia.js: {name,url} per entry, the skipped count and the truncation flag.</summary>
        internal static object Frame(Manifest m) => new
        {
            type = "manifest",
            images = m.Images.Select(e => new { name = e.Name, url = e.Url }),
            videos = m.Videos.Select(e => new { name = e.Name, url = e.Url }),
            skipped = m.Skipped,
            truncated = m.Truncated,
        };

        /// <summary>WPF BackRoomBridge.MediaCount: 1..13, anything else reads as 4.</summary>
        internal static int MediaCount(int? n) => n is >= 1 and <= 13 ? n.Value : 4;

        /// <summary>WPF BackRoomMedia's local deal: the library's .gif/.webp, sorted so the listing order cannot
        /// move it, shuffled with the seed, <paramref name="count"/> of them. None = an empty deal, and the page
        /// falls back to its bundled loops (play.html hostDeal -> localDeal), as WPF's bundled source does.</summary>
        internal static object Deal(Manifest library, string? reqId, int seed, int count)
        {
            var pool = library.Images
                .Where(e => e.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || e.Url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Url, StringComparer.Ordinal).ToList();
            var rng = new Random(seed);
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            var gifs = pool.Take(count).Select((e, i) => new { key = "g" + i, url = e.Url, w = 0, h = 0, src = "local" }).ToList();
            return new
            {
                type = "media", reqId, seed, source = gifs.Count > 0 ? "local" : "bundled",
                gifs, words = Array.Empty<object>(),
            };
        }

        private static IEnumerable<string> Walk(string dir, int depth)
        {
            if (depth > MaxWalkDepth) yield break;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); } catch { yield break; }
            foreach (var f in files) yield return f;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); } catch { yield break; }
            foreach (var d in subs)
            {
                if (Path.GetFileName(d).StartsWith('.')) continue;
                foreach (var f in Walk(d, depth + 1)) yield return f;
            }
        }

        private static bool IsMediaLike(string ext) =>
            ext is ".wmv" or ".avi" or ".mkv" or ".mov" or ".flv" or ".mpg" or ".mpeg" or ".bmp" or ".tiff" or ".heic";

        private static void Downsample<T>(List<T> list, int keep, Random rng)
        {
            if (list.Count <= keep) return;
            for (int i = 0; i < keep; i++)
            {
                int j = i + rng.Next(list.Count - i);
                (list[i], list[j]) = (list[j], list[i]);
            }
            list.RemoveRange(keep, list.Count - keep);
        }
    }
}
