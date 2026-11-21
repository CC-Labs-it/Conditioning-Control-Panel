using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Microsoft.Web.WebView2.Core;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// The player's own pictures and videos for a Just Drop session, served to the live site from
    /// <c>https://justdrop-assets.ccp/&lt;opaque id&gt;.&lt;ext&gt;</c> (handoff-1121/JUSTDROP-WIRING-BRIEF.md).
    ///
    /// <para>One map per window life. Every id is a fresh random token handed out by
    /// <see cref="Take"/>; the request filter answers those ids and nothing else, so no path, no
    /// filename and no guessable name ever reaches the page. Identity is the WHOLE path (or the pack
    /// entry), so a source asked for twice keeps one id. Pack entries decrypt to a temp file on their
    /// first request, streamed (<see cref="ContentPackService.GetPackFileTempPath"/> runs
    /// <c>PackEncryptionService.DecryptFileToFile</c>), and the temp files go when the window does.</para>
    /// </summary>
    internal sealed class JustDropAssetHost
    {
        public const string Host = "justdrop-assets.ccp";
        public const string Origin = "https://" + Host;

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
        private static readonly Regex IdPath = new("^/([0-9a-f]{32})(\\.[a-z0-9]{2,5})$", RegexOptions.CultureInvariant);

        public sealed class Entry
        {
            public required string Ext { get; init; }
            public required bool Video { get; init; }
            public string? DiskPath { get; init; }
            public string? PackId { get; init; }
            public PackFileEntry? PackFile { get; init; }
            internal string? TempPath { get; set; }
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _idByIdentity = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsServable(string ext, bool video)
            => (video ? VideoExts : ImageExts).Contains(ext.ToLowerInvariant());

        /// <summary>Hand out the url for one source (a disk path, or a pack entry). Null when its
        /// extension is not one the page can show.</summary>
        public string? Take(string identity, Entry entry)
        {
            if (string.IsNullOrEmpty(identity) || !IsServable(entry.Ext, entry.Video)) return null;
            lock (_gate)
            {
                if (!_idByIdentity.TryGetValue(identity, out var id))
                {
                    id = Guid.NewGuid().ToString("N");
                    _idByIdentity[identity] = id;
                    _byId[id] = entry;
                }
                return $"{Origin}/{id}{_byId[id].Ext.ToLowerInvariant()}";
            }
        }

        /// <summary>The entry behind a request url, judged on the parsed url: our host, https, an id
        /// this map handed out, and the extension it was handed out with. Anything else is null.</summary>
        public Entry? Resolve(string? requestUri)
        {
            if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var u)) return null;
            if (u.Scheme != Uri.UriSchemeHttps || !string.Equals(u.Host, Host, StringComparison.OrdinalIgnoreCase)
                || !u.IsDefaultPort || u.UserInfo.Length > 0) return null;
            var m = IdPath.Match(u.AbsolutePath);
            if (!m.Success) return null;
            lock (_gate)
            {
                if (!_byId.TryGetValue(m.Groups[1].Value, out var e)) return null;
                return string.Equals(e.Ext, m.Groups[2].Value, StringComparison.OrdinalIgnoreCase) ? e : null;
            }
        }

        // ============================== listing ==============================

        /// <summary>Up to <paramref name="max"/> of the player's own images or videos, shuffled, as
        /// urls. May touch the disk: call off the UI thread.</summary>
        public List<string> List(bool video, int max)
        {
            var sources = video ? ListVideos() : ListImages();
            var rng = new Random();
            for (int i = sources.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (sources[i], sources[j]) = (sources[j], sources[i]); }
            var urls = new List<string>(Math.Min(max, sources.Count));
            foreach (var (identity, entry) in sources)
            {
                if (urls.Count >= max) break;
                if (Take(identity, entry) is { } url) urls.Add(url);
            }
            return urls;
        }

        /// <summary>How many of each the player has (for <c>caps</c>). Off the UI thread.</summary>
        public static (int Images, int Videos) Count() => (ListImages().Count, ListVideos().Count);

        /// <summary>The flash pool's disk half (the same refresh and deselection prune the flashes run)
        /// plus the active packs' images, minus the deselected ones, exactly as FlashService prunes.</summary>
        private static List<(string, Entry)> ListImages()
        {
            var list = new List<(string, Entry)>();
            try
            {
                foreach (var p in App.Flash?.SnapshotLocalImagePaths() ?? new List<string>())
                {
                    var ext = Path.GetExtension(p);
                    if (IsServable(ext, false)) list.Add((Path.GetFullPath(p), new Entry { Ext = ext, Video = false, DiskPath = p }));
                }
                AddPack(list, App.ContentPacks?.GetAllActivePackImages(), video: false);
            }
            catch (Exception ex) { App.Logger?.Debug("JustDropAssets: image listing failed: {E}", ex.Message); }
            return list;
        }

        /// <summary>The For You feed's library walk (mp4/webm/m4v, deselected and repeatedly failing
        /// files left out) plus the active packs' videos.</summary>
        private static List<(string, Entry)> ListVideos()
        {
            var list = new List<(string, Entry)>();
            try
            {
                var root = App.EffectiveAssetsPath;
                foreach (var e in FypAssetManifest.Build(new FypMetaStore()))
                {
                    if (e.Type != "video") continue;
                    var path = Path.GetFullPath(Path.Combine(root, e.Id.Replace('/', Path.DirectorySeparatorChar)));
                    list.Add((path, new Entry { Ext = Path.GetExtension(path), Video = true, DiskPath = path }));
                }
                AddPack(list, App.ContentPacks?.GetAllActivePackVideos(), video: true);
            }
            catch (Exception ex) { App.Logger?.Debug("JustDropAssets: video listing failed: {E}", ex.Message); }
            return list;
        }

        private static void AddPack(List<(string, Entry)> into, List<(string PackId, PackFileEntry File)>? pack, bool video)
        {
            if (pack == null) return;
            var disabled = App.Settings?.Current?.DisabledAssetPaths;
            foreach (var (packId, file) in pack)
            {
                if (disabled != null && disabled.Contains($"pack:{packId}/{file.OriginalName}")) continue;
                if (!IsServable(file.Extension ?? "", video)) continue;
                into.Add(($"pack:{packId}/{file.ObfuscatedName}",
                    new Entry { Ext = file.Extension!, Video = video, PackId = packId, PackFile = file }));
            }
        }

        // ============================== serving ==============================

        /// <summary>Put the request filter on this window's core. Call from OnCoreCreated.</summary>
        public void Attach(CoreWebView2 core)
        {
            core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => OnRequest(core, e);
        }

        private void OnRequest(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var uri = e.Request.Uri;
                // Every filter on this core raises this event (the Scrolller gate, the sign-in header):
                // act on our host only.
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !string.Equals(u.Host, Host, StringComparison.OrdinalIgnoreCase)) return;
                var env = core.Environment;
                if (string.Equals(e.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    e.Response = env.CreateWebResourceResponse(null, 204, "No Content",
                        Cors("Access-Control-Allow-Methods: GET, HEAD\r\nAccess-Control-Allow-Headers: Range"));
                    return;
                }
                var entry = Resolve(uri);
                if (entry == null)
                {
                    e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", Cors());
                    return;
                }
                string? range = null;
                try { range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null; } catch { }
                var deferral = e.GetDeferral();
                _ = ServeAsync(core, e, deferral, entry, range);
            }
            catch (Exception ex) { App.Logger?.Debug("JustDropAssets: request hook failed: {E}", ex.Message); }
        }

        private async Task ServeAsync(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e,
            CoreWebView2Deferral deferral, Entry entry, string? range)
        {
            try
            {
                var read = await Task.Run(() =>
                {
                    var path = PathFor(entry);
                    return path == null ? null : ScrolllerWebGate.OpenCached(path, range);
                });
                var env = core.Environment;
                var type = ScrolllerWebGate.ContentTypeFor(Origin + "/a" + entry.Ext.ToLowerInvariant());
                if (read == null)
                    e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", Cors());
                else if (read.Value.Unsatisfiable)
                    e.Response = env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable",
                        Cors($"Content-Range: bytes */{read.Value.Total}"));
                else if (read.Value.Range is { } r)
                    e.Response = env.CreateWebResourceResponse(read.Value.Body, 206, "Partial Content",
                        Cors($"Content-Type: {type}\r\nContent-Length: {r.Length}\r\nAccept-Ranges: bytes\r\n"
                            + $"Content-Range: bytes {r.Start}-{r.End}/{read.Value.Total}\r\nCache-Control: no-store"));
                else
                    e.Response = env.CreateWebResourceResponse(read.Value.Body, 200, "OK",
                        Cors($"Content-Type: {type}\r\nContent-Length: {read.Value.Total}\r\nAccept-Ranges: bytes\r\nCache-Control: no-store"));
            }
            catch (Exception ex) { App.Logger?.Debug("JustDropAssets: answer failed: {E}", ex.Message); }
            finally { try { deferral.Complete(); } catch { } }
        }

        /// <summary>The file behind an entry: the disk path, or the pack entry's temp copy, decrypted
        /// on the first ask (streamed, never whole in memory).</summary>
        private static string? PathFor(Entry entry)
        {
            if (entry.DiskPath != null) return entry.DiskPath;
            lock (entry)
            {
                if (entry.TempPath != null && File.Exists(entry.TempPath)) return entry.TempPath;
                if (entry.PackId == null || entry.PackFile == null) return null;
                entry.TempPath = App.ContentPacks?.GetPackFileTempPath(entry.PackId, entry.PackFile);
                return entry.TempPath;
            }
        }

        /// <summary>Headers every answer carries: the site may read it (a canvas stays untainted).</summary>
        internal static string Cors(string? extra = null)
            => (string.IsNullOrEmpty(extra) ? "" : extra + "\r\n")
            + $"Access-Control-Allow-Origin: {JustDropBridge.SiteOrigin}\r\nVary: Origin\r\nCross-Origin-Resource-Policy: cross-origin";

        /// <summary>Window closed: forget every id and delete the decrypted pack copies.</summary>
        public void Clear()
        {
            List<Entry> entries;
            lock (_gate) { entries = _byId.Values.ToList(); _byId.Clear(); _idByIdentity.Clear(); }
            foreach (var e in entries)
            {
                string? temp;
                lock (e) { temp = e.TempPath; e.TempPath = null; }
                if (temp != null) MediaTempFileLedger.TryDeleteFile(temp);
            }
        }
    }
}
