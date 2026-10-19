using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>What a cached Scrolller file is, kept beside it so a reuse pick can honour the
/// user's niches. <see cref="Kind"/> is "clip", "gif", "picture" or "poster".</summary>
internal sealed record ScrolllerCacheMeta(string Kind, string? Sub = null, string? Poster = null,
    int? Width = null, int? Height = null, string? Title = null);

/// <summary>One complete cached file.</summary>
internal sealed record ScrolllerCacheEntry(string Url, string Path, long Bytes, DateTime LastUsedUtc, ScrolllerCacheMeta Meta);

/// <summary>A download being written into the cache as it is read (a tee). Dispose without
/// <see cref="Commit"/> throws the partial file away.</summary>
internal interface IScrolllerCacheWriter : IDisposable
{
    Stream Stream { get; }
    /// <summary>The download finished whole: the file becomes a cache entry.</summary>
    void Commit();
}

/// <summary>
/// The cache's books, pure (no disk) so the LRU rules are pinned by tests: which urls are held,
/// how many bytes, when each was last used, and which entries go when the cap is passed.
/// Not thread-safe on its own; <see cref="ScrolllerDiskCache"/> holds its lock around it.
/// </summary>
internal sealed class ScrolllerCacheBook
{
    private readonly Dictionary<string, ScrolllerCacheEntry> _byUrl = new(StringComparer.Ordinal);

    public long TotalBytes { get; private set; }
    public int Count => _byUrl.Count;

    public ScrolllerCacheEntry? Get(string url) => _byUrl.TryGetValue(url, out var e) ? e : null;

    public void Put(ScrolllerCacheEntry entry)
    {
        Remove(entry.Url);
        _byUrl[entry.Url] = entry;
        TotalBytes += entry.Bytes;
    }

    public ScrolllerCacheEntry? Remove(string url)
    {
        if (!_byUrl.Remove(url, out var old)) return null;
        TotalBytes -= old.Bytes;
        return old;
    }

    public ScrolllerCacheEntry? Touch(string url, DateTime utc)
    {
        if (!_byUrl.TryGetValue(url, out var e)) return null;
        var touched = e with { LastUsedUtc = utc };
        _byUrl[url] = touched;
        return touched;
    }

    /// <summary>Most recently used first.</summary>
    public List<ScrolllerCacheEntry> Snapshot()
        => _byUrl.Values.OrderByDescending(e => e.LastUsedUtc).ToList();

    /// <summary>Which entries to drop, oldest use first, until the total fits under
    /// <paramref name="capBytes"/>. <paramref name="keepUrl"/> (the file just written) is never
    /// picked: a fresh download must not evict itself.</summary>
    public static List<ScrolllerCacheEntry> Evictions(IEnumerable<ScrolllerCacheEntry> entries, long capBytes, string? keepUrl)
    {
        var all = entries.ToList();
        long total = all.Sum(e => e.Bytes);
        var drop = new List<ScrolllerCacheEntry>();
        if (total <= capBytes) return drop;
        foreach (var e in all.OrderBy(e => e.LastUsedUtc))
        {
            if (total <= capBytes) break;
            if (keepUrl != null && string.Equals(e.Url, keepUrl, StringComparison.Ordinal)) continue;
            drop.Add(e);
            total -= e.Bytes;
        }
        return drop;
    }

    /// <summary>A download becomes an entry only when it reached the end of the body, carried at
    /// least one byte, and (when the server said how long it was) carried exactly that many.
    /// Anything else is a partial file and is thrown away.</summary>
    public static bool IsWhole(long received, long? declared, bool reachedEnd)
        => reachedEnd && received > 0 && (declared is not long d || d == received);

    // ---- index file -----------------------------------------------------------------

    public string ToJson()
    {
        var arr = new JArray();
        foreach (var e in Snapshot())
        {
            var m = e.Meta;
            arr.Add(new JObject
            {
                ["u"] = e.Url, ["f"] = System.IO.Path.GetFileName(e.Path), ["b"] = e.Bytes,
                ["t"] = e.LastUsedUtc.Ticks, ["k"] = m.Kind, ["s"] = m.Sub, ["p"] = m.Poster,
                ["w"] = m.Width, ["h"] = m.Height, ["ti"] = m.Title,
            });
        }
        return new JObject { ["v"] = 1, ["entries"] = arr }.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>Index text -> book, paths under <paramref name="root"/>. A record whose file name
    /// is not one this cache makes is dropped (the index can never point outside the folder).
    /// Bad text = an empty book.</summary>
    public static ScrolllerCacheBook FromJson(string? json, string root)
    {
        var book = new ScrolllerCacheBook();
        if (string.IsNullOrWhiteSpace(json)) return book;
        try
        {
            var o = JObject.Parse(json);
            if (o["entries"] is not JArray arr) return book;
            foreach (var t in arr.OfType<JObject>())
            {
                var url = (string?)t["u"];
                var file = (string?)t["f"];
                var bytes = (long?)t["b"] ?? 0;
                if (string.IsNullOrEmpty(url) || file == null || !ScrolllerDiskCache.IsCacheFileName(file) || bytes <= 0) continue;
                var meta = new ScrolllerCacheMeta((string?)t["k"] ?? "picture", (string?)t["s"], (string?)t["p"],
                    (int?)t["w"], (int?)t["h"], (string?)t["ti"]);
                var ticks = (long?)t["t"] ?? 0;
                var when = ticks > 0 && ticks < DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
                book.Put(new ScrolllerCacheEntry(url!, System.IO.Path.Combine(root, file), bytes, when, meta));
            }
        }
        catch { return new ScrolllerCacheBook(); }
        return book;
    }
}

/// <summary>
/// Scrolller media kept on the USER'S disk across sessions and restarts (owner, 2026-10-19: reuse
/// what was already seen, so the daily share stretches and Scrolller serves less). A replay from
/// here costs nothing against <see cref="ScrolllerAllowance"/>. Bounded LRU, about
/// <see cref="DefaultCapBytes"/>, under the assets drive. The Scrolller licence covers keeping
/// media cached on the user's device; nothing here ever leaves this machine.
///
/// CONTRACT (lanes build against these signatures; the cache lane owns the implementation):
/// every member is thread-safe and never throws. A fresh download honours
/// <see cref="ScrolllerPolicyService.MayFetchFresh"/>, sends the honest user-agent, and charges
/// the allowance with the bytes actually received.
///
/// Layout: <c>{EffectiveAssetsPath}/.scrolller-cache/</c>, one file per url named by the url's
/// sha256 plus its extension, and <c>index.json</c> (url, bytes, last use, label). A download is
/// written to a <c>.part</c> file and renamed only once whole, so a partial file never becomes an
/// entry; the first use after launch sweeps leftovers (parts, files the index does not know).
/// NOT the <c>ccp_temp_</c> scheme on purpose: those are swept at every launch.
/// </summary>
internal static class ScrolllerDiskCache
{
    public const long DefaultCapBytes = 2048L * 1024 * 1024;

    /// <summary>One file larger than this is never kept (a feed clip is far smaller).</summary>
    public const long MaxEntryBytes = 400L * 1024 * 1024;

    internal const string FolderName = ".scrolller-cache";
    internal const string IndexFileName = "index.json";
    internal const string PartSuffix = ".part";

    /// <summary>Charge the allowance every this many bytes while a body flows, and the rest at
    /// the end (a cancelled download still pays for what came). One charge per read would fire
    /// hundreds of Changed events per clip.</summary>
    internal const int ChargeStepBytes = 512 * 1024;

    private static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(3);
    private static readonly Regex CacheFile = new("^[0-9a-f]{64}\\.[a-z0-9]{1,5}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeExt = new("^\\.[a-z0-9]{1,5}$", RegexOptions.CultureInvariant);

    /// <summary>The cache folder, or null when there is none. Replaceable by tests.</summary>
    internal static Func<string?> RootProvider = () =>
    {
        try { return Path.Combine(App.EffectiveAssetsPath, FolderName); }
        catch { return null; }
    };

    /// <summary>Byte cap. Replaceable by tests.</summary>
    internal static long CapBytes = DefaultCapBytes;

    /// <summary>The clock for last-use stamps. Replaceable by tests.</summary>
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

    private static readonly object Gate = new();
    private static ScrolllerCacheBook? _book;
    private static string? _bookRoot;
    private static Timer? _saveTimer;
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> InFlight = new(StringComparer.Ordinal);
    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var c = RemoteMediaCache.CreateClient();   // honest user-agent, no Referer
        c.Timeout = DownloadBudget;
        return c;
    });

    // ---- contract -------------------------------------------------------------------

    /// <summary>Path of a complete cached copy of <paramref name="url"/>, or null. A hit counts as
    /// a use (LRU). Null while the switch is off: cached copies stop too.</summary>
    public static string? TryGetPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || !ScrolllerPolicy.Enabled) return null;
        try
        {
            string? path;
            lock (Gate)
            {
                var book = Book();
                var e = book?.Get(url!);
                if (book == null || e == null) return null;
                if (!File.Exists(e.Path)) { book.Remove(url!); path = null; }
                else { book.Touch(url!, UtcNow()); path = e.Path; }
            }
            ScheduleSave();
            return path;
        }
        catch { return null; }
    }

    /// <summary>True when a complete copy is cached (no LRU touch). False while the switch is off.</summary>
    public static bool Contains(string? url)
    {
        if (string.IsNullOrEmpty(url) || !ScrolllerPolicy.Enabled) return false;
        try { lock (Gate) return Book()?.Get(url!) != null; }
        catch { return false; }
    }

    /// <summary>The cached copy, else a fresh download into the cache (refused, null, when
    /// <see cref="ScrolllerPolicyService.MayFetchFresh"/> says no). Null on any failure.
    /// One download per url however many ask; the first asker's token can cancel it, a later
    /// asker only stops waiting.</summary>
    public static async Task<string?> GetOrFetchAsync(string url, ScrolllerCacheMeta? meta, CancellationToken ct)
    {
        try
        {
            var hit = TryGetPath(url);
            if (hit != null) return hit;
            if (!global::ConditioningControlPanel.Services.Video.ScrolllerCredit.IsScrolllerUrl(url)
                || !ScrolllerPolicyService.MayFetchFresh(url)) return null;

            Lazy<Task<string?>>? mine = null;
            mine = new Lazy<Task<string?>>(() => Task.Run(async () =>
            {
                try { return await DownloadAsync(url, meta, ct).ConfigureAwait(false); }
                finally { InFlight.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(url, mine!)); }
            }));
            var running = InFlight.GetOrAdd(url, mine);
            return await running.Value.WaitAsync(ct).ConfigureAwait(false);
        }
        catch { return null; }
    }

    /// <summary>Starts a tee for a download the caller makes itself (the caller charges the
    /// allowance). Null when the cache cannot take it.</summary>
    public static IScrolllerCacheWriter? BeginWrite(string url, ScrolllerCacheMeta? meta)
    {
        if (string.IsNullOrEmpty(url) || !ScrolllerPolicy.Enabled) return null;
        try
        {
            string? root;
            lock (Gate) { root = Book() != null ? _bookRoot : null; }
            if (root == null) return null;
            var part = Path.Combine(root, FileNameFor(url) + "." + Guid.NewGuid().ToString("N") + PartSuffix);
            var fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            return new Writer(url, ResolveMeta(url, meta), part, fs);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[scrolller cache] write refused: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>Complete entries, most recently used first, optionally filtered. For reuse picks.
    /// Empty while the switch is off.</summary>
    public static IReadOnlyList<ScrolllerCacheEntry> Entries(Func<ScrolllerCacheEntry, bool>? filter = null)
    {
        if (!ScrolllerPolicy.Enabled) return Array.Empty<ScrolllerCacheEntry>();
        try
        {
            List<ScrolllerCacheEntry> all;
            lock (Gate) all = Book()?.Snapshot() ?? new List<ScrolllerCacheEntry>();
            if (filter == null) return all;
            var picked = new List<ScrolllerCacheEntry>();
            foreach (var e in all)
            {
                try { if (filter(e)) picked.Add(e); } catch { }
            }
            return picked;
        }
        catch { return Array.Empty<ScrolllerCacheEntry>(); }
    }

    /// <summary>Writes the index now (app exit). Never throws.</summary>
    public static void Flush()
    {
        try { _saveTimer?.Dispose(); } catch { }
        _saveTimer = null;
        Save();
    }

    // ---- pure helpers ---------------------------------------------------------------

    /// <summary>sha256 of the url, lowercase hex, plus the url's extension (".bin" when it has
    /// none a player could use).</summary>
    internal static string FileNameFor(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return hash + ExtensionOf(url);
    }

    internal static string ExtensionOf(string url)
    {
        try
        {
            int cut = url.IndexOfAny(new[] { '?', '#' });
            var ext = Path.GetExtension(cut >= 0 ? url[..cut] : url).ToLowerInvariant();
            return SafeExt.IsMatch(ext) ? ext : ".bin";
        }
        catch { return ".bin"; }
    }

    internal static bool IsCacheFileName(string name) => CacheFile.IsMatch(name);

    /// <summary>The label a file is stored with: the caller's, else what the feed said, else a
    /// guess from the extension.</summary>
    internal static ScrolllerCacheMeta ResolveMeta(string url, ScrolllerCacheMeta? meta)
    {
        if (meta != null) return meta;
        var known = ScrolllerItemIndex.MetaFor(url);
        if (known != null) return known;
        if (ExtensionOf(url) == ".gif") return new ScrolllerCacheMeta("gif");
        if (RemoteMediaFormats.IsRemoteVideo(url)) return new ScrolllerCacheMeta("clip");
        return new ScrolllerCacheMeta("picture");
    }

    // ---- disk -----------------------------------------------------------------------

    /// <summary>The book for the current root, loaded (and the folder swept) on first use. Null
    /// when there is no folder. Caller holds <see cref="Gate"/>.</summary>
    private static ScrolllerCacheBook? Book()
    {
        var root = RootProvider();
        if (string.IsNullOrEmpty(root)) return null;
        if (_book != null && string.Equals(root, _bookRoot, StringComparison.OrdinalIgnoreCase)) return _book;

        Directory.CreateDirectory(root);
        var indexPath = Path.Combine(root, IndexFileName);
        string? json = null;
        try { if (File.Exists(indexPath)) json = File.ReadAllText(indexPath); } catch { }
        var book = ScrolllerCacheBook.FromJson(json, root);

        // Records whose file is gone or has the wrong size go; files the index does not know
        // (a crash mid-download, an eviction that hit an open file) are deleted.
        foreach (var e in book.Snapshot())
        {
            try
            {
                var fi = new FileInfo(e.Path);
                if (!fi.Exists || fi.Length != e.Bytes) book.Remove(e.Url);
            }
            catch { book.Remove(e.Url); }
        }
        var known = new HashSet<string>(book.Snapshot().Select(e => Path.GetFileName(e.Path)), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.GetFiles(root))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, IndexFileName, StringComparison.OrdinalIgnoreCase) || known.Contains(name)) continue;
                TryDelete(file);
            }
        }
        catch { }

        _book = book;
        _bookRoot = root;
        EvictLocked(null);
        return book;
    }

    /// <summary>Drops oldest entries past the cap. Caller holds <see cref="Gate"/>. A file still
    /// open (a player has it) cannot be deleted: it leaves the books anyway and the next launch's
    /// sweep removes it.</summary>
    private static void EvictLocked(string? keepUrl)
    {
        if (_book == null) return;
        foreach (var e in ScrolllerCacheBook.Evictions(_book.Snapshot(), CapBytes, keepUrl))
        {
            _book.Remove(e.Url);
            TryDelete(e.Path);
        }
    }

    /// <summary>A whole part file becomes the entry for <paramref name="url"/>. Returns its path,
    /// or null (the part is deleted).</summary>
    private static string? CommitPart(string url, ScrolllerCacheMeta meta, string part)
    {
        try
        {
            var len = new FileInfo(part).Length;
            if (len <= 0 || len > MaxEntryBytes) { TryDelete(part); return null; }
            string final;
            lock (Gate)
            {
                var book = Book();
                if (book == null || _bookRoot == null) { TryDelete(part); return null; }
                final = Path.Combine(_bookRoot, FileNameFor(url));
                try { File.Move(part, final, overwrite: true); }
                catch
                {
                    // The same url is already cached and open in a player: keep that copy.
                    TryDelete(part);
                    var existing = book.Get(url);
                    if (existing != null && File.Exists(existing.Path)) { book.Touch(url, UtcNow()); return existing.Path; }
                    return null;
                }
                book.Put(new ScrolllerCacheEntry(url, final, len, UtcNow(), meta));
                EvictLocked(url);
            }
            ScheduleSave();
            return final;
        }
        catch (Exception ex)
        {
            TryDelete(part);
            App.Logger?.Debug("[scrolller cache] commit failed: {E}", ex.Message);
            return null;
        }
    }

    private static async Task<string?> DownloadAsync(string url, ScrolllerCacheMeta? meta, CancellationToken ct)
    {
        long uncharged = 0;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DownloadBudget);
            using var resp = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                App.Logger?.Debug("[scrolller cache] HTTP {Code} for {Host}", (int)resp.StatusCode, Logging.UrlLog.Host(url));
                return null;
            }
            long? declared = resp.Content.Headers.ContentLength;
            if (declared > MaxEntryBytes) return null;

            using var writer = BeginWrite(url, meta) as Writer;
            if (writer == null) return null;
            using var src = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var buf = new byte[81920];
            long got = 0;
            while (true)
            {
                int n = await src.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token).ConfigureAwait(false);
                if (n <= 0) break;
                got += n;
                uncharged += n;
                if (uncharged >= ChargeStepBytes) { ScrolllerAllowance.Charge(uncharged); uncharged = 0; }
                if (got > MaxEntryBytes) return null;
                await writer.Stream.WriteAsync(buf.AsMemory(0, n), cts.Token).ConfigureAwait(false);
            }
            if (!ScrolllerCacheBook.IsWhole(got, declared, reachedEnd: true)) return null;
            return writer.CommitAndGetPath();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[scrolller cache] download from {Host} failed: {E}", Logging.UrlLog.Host(url), ex.Message);
            return null;
        }
        finally
        {
            if (uncharged > 0) ScrolllerAllowance.Charge(uncharged);
        }
    }

    private sealed class Writer : IScrolllerCacheWriter
    {
        private readonly string _url;
        private readonly ScrolllerCacheMeta _meta;
        private readonly string _part;
        private readonly FileStream _fs;
        private int _done;

        public Writer(string url, ScrolllerCacheMeta meta, string part, FileStream fs)
        {
            _url = url; _meta = meta; _part = part; _fs = fs;
        }

        public Stream Stream => _fs;

        public void Commit() => CommitAndGetPath();

        public string? CommitAndGetPath()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return null;
            try { _fs.Flush(); _fs.Dispose(); }
            catch { TryDelete(_part); return null; }
            return CommitPart(_url, _meta, _part);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            try { _fs.Dispose(); } catch { }
            TryDelete(_part);
        }
    }

    private static void ScheduleSave()
    {
        try
        {
            if (_saveTimer != null) return;
            _saveTimer = new Timer(_ => { _saveTimer?.Dispose(); _saveTimer = null; Save(); }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }
        catch { }
    }

    private static void Save()
    {
        try
        {
            string json, path;
            lock (Gate)
            {
                if (_book == null || _bookRoot == null) return;
                json = _book.ToJson();
                path = Path.Combine(_bookRoot, IndexFileName);
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] index save failed: {E}", ex.Message); }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Tests only: forget the books (the folder stays).</summary>
    internal static void ResetForTests()
    {
        try { _saveTimer?.Dispose(); } catch { }
        _saveTimer = null;
        lock (Gate) { _book = null; _bookRoot = null; }
        InFlight.Clear();
        CapBytes = DefaultCapBytes;
    }
}
