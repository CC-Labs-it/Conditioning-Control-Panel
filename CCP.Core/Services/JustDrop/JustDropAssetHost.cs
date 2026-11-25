using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.MediaFiles;
using Serilog;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// WPF JustDropAssetHost.cs:25-252: the player's own pictures and videos for one Just Drop window,
    /// served from <c>https://justdrop-assets.ccp/&lt;opaque id&gt;.&lt;ext&gt;</c>. Every id is a fresh
    /// random token; only ids this map handed out are answered. Pack entries decrypt to a temp file on
    /// their first request and the temp files go on <see cref="Clear"/>. The transport (a WebView2 filter)
    /// lives in the head and only relays <see cref="Answer"/>.
    /// </summary>
    internal sealed class JustDropAssetMap
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

        /// <summary>One answer for the transport: status, reason, CRLF-joined headers, body (caller disposes).</summary>
        public readonly record struct Response(int Status, string Reason, string Headers, Stream? Body);

        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _idByIdentity = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<bool, List<(string Identity, Entry Entry)>> _sources;
        private readonly Func<Entry, string?> _decrypt;
        private readonly Action<string> _deleteTemp;

        /// <param name="sources">The player's sources per kind (tests); defaults to the live pools.</param>
        /// <param name="decrypt">A pack entry to a fresh temp file (tests); defaults to the active pack store.</param>
        /// <param name="deleteTemp">Deletes one decrypted copy (tests); defaults to the pack store's delete.</param>
        public JustDropAssetMap(Func<bool, List<(string, Entry)>>? sources = null,
            Func<Entry, string?>? decrypt = null, Action<string>? deleteTemp = null)
        {
            _sources = sources ?? (video => video ? ListVideos() : ListImages());
            _decrypt = decrypt ?? (e => ContentPackStore.Current?.GetPackFileTempPath(e.PackId!, e.PackFile!));
            _deleteTemp = deleteTemp ?? (p =>
            {
                if (ContentPackStore.Current is { } store) store.DeleteTempFile(p);
                else MediaTempFileLedger.TryDeleteFile(p);
            });
        }

        public static bool IsServable(string ext, bool video)
            => (video ? VideoExts : ImageExts).Contains(ext.ToLowerInvariant());

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

        // ============================== listing (WPF :87-154) ==============================

        /// <summary>Up to <paramref name="max"/> of the player's own images or videos, shuffled, as urls. Off the UI thread.</summary>
        public List<string> List(bool video, int max)
        {
            var sources = _sources(video);
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

        /// <summary>The flash pool's disk half (images folder, excluded folders left out) plus the active packs' images.</summary>
        private static List<(string, Entry)> ListImages()
        {
            var list = new List<(string, Entry)>();
            try
            {
                var root = CorePaths.EffectiveAssets;
                var dir = Path.Combine(root, "images");
                if (Directory.Exists(dir))
                {
                    var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                        .Where(f => IsServable(Path.GetExtension(f), false)).ToList();
                    foreach (var p in AssetFolderExclusion.Enabled(files, root, CoreSettings.Current))
                        list.Add((Path.GetFullPath(p), new Entry { Ext = Path.GetExtension(p), Video = false, DiskPath = p }));
                }
                AddPack(list, ContentPackStore.Current?.GetAllActivePackImages(), video: false);
            }
            catch (Exception ex) { Log.Debug("JustDropAssets: image listing failed: {E}", ex.Message); }
            return list;
        }

        /// <summary>The For You feed's library walk plus the active packs' videos.</summary>
        private static List<(string, Entry)> ListVideos()
        {
            var list = new List<(string, Entry)>();
            try
            {
                var root = CorePaths.EffectiveAssets;
                foreach (var e in FypAssetManifest.Build(new FypMetaStore()))
                {
                    if (e.Type != "video") continue;
                    var path = Path.GetFullPath(Path.Combine(root, e.Id.Replace('/', Path.DirectorySeparatorChar)));
                    list.Add((path, new Entry { Ext = Path.GetExtension(path), Video = true, DiskPath = path }));
                }
                AddPack(list, ContentPackStore.Current?.GetAllActivePackVideos(), video: true);
            }
            catch (Exception ex) { Log.Debug("JustDropAssets: video listing failed: {E}", ex.Message); }
            return list;
        }

        // The store's active pools already leave out the per-file opt-outs (ContentPackStore.ActivePool).
        private static void AddPack(List<(string, Entry)> into, List<(string PackId, PackFileEntry File)>? pack, bool video)
        {
            if (pack == null) return;
            foreach (var (packId, file) in pack)
            {
                if (!IsServable(file.Extension ?? "", video)) continue;
                into.Add(($"pack:{packId}/{file.ObfuscatedName}",
                    new Entry { Ext = file.Extension!, Video = video, PackId = packId, PackFile = file }));
            }
        }

        // ============================== serving (WPF :158-237) ==============================

        /// <summary>The answer for one request to our host, or null when the url is not our host
        /// (another filter's request). Touches the disk: off the UI thread.</summary>
        public Response? Answer(string? method, string? requestUri, string? range)
        {
            if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var u) || !string.Equals(u.Host, Host, StringComparison.OrdinalIgnoreCase))
                return null;
            if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                return new Response(204, "No Content", Cors("Access-Control-Allow-Methods: GET, HEAD\r\nAccess-Control-Allow-Headers: Range"), null);
            var entry = Resolve(requestUri);
            var path = entry == null ? null : PathFor(entry);
            var read = path == null ? null : ScrolllerWebGate.OpenCached(path, range);
            if (read == null) return new Response(404, "Not Found", Cors(), null);
            var r = read.Value;
            if (r.Unsatisfiable) return new Response(416, "Range Not Satisfiable", Cors($"Content-Range: bytes */{r.Total}"), null);
            var type = ScrolllerWebGate.ContentTypeFor(Origin + "/a" + entry!.Ext.ToLowerInvariant());
            if (r.Range is { } part)
                return new Response(206, "Partial Content", Cors($"Content-Type: {type}\r\nContent-Length: {part.Length}\r\nAccept-Ranges: bytes\r\n"
                    + $"Content-Range: bytes {part.Start}-{part.End}/{r.Total}\r\nCache-Control: no-store"), r.Body);
            return new Response(200, "OK", Cors($"Content-Type: {type}\r\nContent-Length: {r.Total}\r\nAccept-Ranges: bytes\r\nCache-Control: no-store"), r.Body);
        }

        /// <summary>The disk path, or the pack entry's temp copy decrypted on the first ask.</summary>
        private string? PathFor(Entry entry)
        {
            if (entry.DiskPath != null) return entry.DiskPath;
            lock (entry)
            {
                if (entry.TempPath != null && File.Exists(entry.TempPath)) return entry.TempPath;
                if (entry.PackId == null || entry.PackFile == null) return null;
                entry.TempPath = _decrypt(entry);
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
                if (temp != null) _deleteTemp(temp);
            }
        }
    }
}
