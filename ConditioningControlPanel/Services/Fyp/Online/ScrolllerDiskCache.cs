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

/// <summary>One complete cached file. <see cref="DownloadedUtc"/> is when its bytes came from
/// Scrolller (never moved by a use): the 24 h max age counts from it. MinValue = unknown = expired.</summary>
internal sealed record ScrolllerCacheEntry(string Url, string Path, long Bytes, DateTime LastUsedUtc, ScrolllerCacheMeta Meta,
    DateTime DownloadedUtc);

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

    /// <summary>Every row has passed the byte check (index field "fc"). An index written by a build
    /// without the check drops the field, so its rows get checked again.</summary>
    public bool FormatChecked { get; set; }
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

    /// <summary>The licence rule (owner, 2026-10-21): a copy is kept at most
    /// <see cref="ScrolllerDiskCache.MaxAge"/> from its DOWNLOAD, however recently it was used. No
    /// stamp (a row from a build before the rule) is expired, and so is a stamp more than an hour
    /// in the future (the clock went back: never a copy that lives forever).</summary>
    public static bool IsExpiredAt(DateTime downloadedUtc, DateTime nowUtc)
        => downloadedUtc <= DateTime.MinValue
           || downloadedUtc - nowUtc > TimeSpan.FromHours(1)
           || nowUtc - downloadedUtc >= ScrolllerDiskCache.MaxAge;

    public static bool IsExpired(ScrolllerCacheEntry entry, DateTime nowUtc) => IsExpiredAt(entry.DownloadedUtc, nowUtc);

    /// <summary>Every row past the max age at <paramref name="nowUtc"/>.</summary>
    public List<ScrolllerCacheEntry> Expired(DateTime nowUtc) => _byUrl.Values.Where(e => IsExpired(e, nowUtc)).ToList();

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
                ["w"] = m.Width, ["h"] = m.Height, ["ti"] = m.Title, ["d"] = e.DownloadedUtc.Ticks,
            });
        }
        var root = new JObject { ["v"] = 2, ["entries"] = arr };
        if (FormatChecked) root["fc"] = 1;
        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>Index text -> book, paths under <paramref name="root"/>. A record whose file name
    /// is not one this cache makes is dropped (the index can never point outside the folder).
    /// A v1 row (no "d") reads with no download stamp, which <see cref="IsExpired"/> calls expired.
    /// Bad text = an empty book.</summary>
    public static ScrolllerCacheBook FromJson(string? json, string root)
    {
        var book = new ScrolllerCacheBook();
        if (string.IsNullOrWhiteSpace(json)) return book;
        try
        {
            var o = JObject.Parse(json);
            if (o["entries"] is not JArray arr) return book;
            book.FormatChecked = (int?)o["fc"] == 1;
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
                var dTicks = (long?)t["d"] ?? 0;
                var got = dTicks > 0 && dTicks < DateTime.MaxValue.Ticks ? new DateTime(dTicks, DateTimeKind.Utc) : DateTime.MinValue;
                book.Put(new ScrolllerCacheEntry(url!, System.IO.Path.Combine(root, file), bytes, when, meta, got));
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
/// entry; the load at launch (<see cref="WarmAsync"/>, in the background) sweeps leftovers (parts,
/// files the index does not know). Until it is done the cache reads as empty.
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

    /// <summary>The clock for last-use and download stamps. Replaceable by tests.</summary>
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

    /// <summary>Licence rule (owner, 2026-10-21): no Scrolller copy outlives a day from its download.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

    /// <summary>The max age in force. Replaceable by tests; never raised in the app.</summary>
    internal static TimeSpan MaxAge = DefaultMaxAge;

    /// <summary>How often the running app sweeps every Scrolller copy (<see cref="ScrolllerCopySweeper"/>).</summary>
    internal static readonly TimeSpan SweepPeriod = TimeSpan.FromHours(1);

    /// <summary>A part file younger than this may still be downloading: the sweep leaves it.</summary>
    private static readonly TimeSpan PartGrace = TimeSpan.FromMinutes(10);

    /// <summary>The user's remote-media consent (MediaSource not local AND HasRemoteMediaConsent).
    /// Replaceable by tests.</summary>
    internal static Func<bool> RemoteConsent = () =>
    {
        var s = App.Settings?.Current;
        return s != null && s.MediaSource != "local" && s.HasRemoteMediaConsent;
    };

    /// <summary>Tests only: load the books on the caller's thread instead of in the background.</summary>
    internal static bool WarmInline;

    private static readonly object Gate = new();
    private static ScrolllerCacheBook? _book;
    private static string? _bookRoot;
    private static Task? _warm;
    private static string? _warmRoot;
    private static DateTime _warmStartedUtc;
    private static readonly object SaveLock = new();  // the save timer field
    private static readonly object FileLock = new();  // one writer of index.json.tmp at a time
    private static long _saveSeq, _writtenSeq;        // a stale snapshot never overwrites a newer one
    private static Timer? _saveTimer;
    private static Timer? _sweepTimer;
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> InFlight = new(StringComparer.Ordinal);
    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var c = RemoteMediaCache.CreateClient();   // honest user-agent, no Referer
        c.Timeout = DownloadBudget;
        return c;
    });

    // ---- contract -------------------------------------------------------------------

    /// <summary>Path of a complete cached copy of <paramref name="url"/>, or null. A hit counts as
    /// a use (LRU). Null while the switch is off: cached copies stop too. A copy past the max age
    /// is a miss and is deleted on the spot.</summary>
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
                if (DropIfExpiredLocked(book, e) || !File.Exists(e.Path)) { book.Remove(url!); path = null; }
                else { book.Touch(url!, UtcNow()); path = e.Path; }
            }
            ScheduleSave();
            return path;
        }
        catch { return null; }
    }

    /// <summary>True when a complete copy is cached, not past the max age, and its file is still
    /// there (no LRU touch). False while the switch is off.</summary>
    public static bool Contains(string? url)
    {
        if (string.IsNullOrEmpty(url) || !ScrolllerPolicy.Enabled) return false;
        try
        {
            string? path;
            bool dropped = false;
            lock (Gate)
            {
                var book = Book();
                var e = book?.Get(url!);
                if (book != null && e != null && DropIfExpiredLocked(book, e)) { dropped = true; e = null; }
                path = e?.Path;
            }
            if (dropped) ScheduleSave();
            return path != null && File.Exists(path);
        }
        catch { return false; }
    }

    /// <summary>When the bytes of <paramref name="url"/>'s cached copy came from Scrolller, or
    /// null. For a copy held elsewhere (memory) so it expires with the disk one, not a day later.</summary>
    internal static DateTime? DownloadedUtcOf(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try { lock (Gate) return Book()?.Get(url!)?.DownloadedUtc; }
        catch { return null; }
    }

    /// <summary>The max-age rule on one row: an expired row leaves the book and its file is
    /// deleted (a file a player still holds stays until the orphan sweep: the row is gone, so it
    /// never serves again). True when it was expired. Caller holds <see cref="Gate"/>.</summary>
    private static bool DropIfExpiredLocked(ScrolllerCacheBook book, ScrolllerCacheEntry e)
    {
        if (!ScrolllerCacheBook.IsExpired(e, UtcNow())) return false;
        book.Remove(e.Url);
        TryDelete(e.Path);
        return true;
    }

    /// <summary>The cached copy, else a fresh download into the cache (refused, null, when
    /// <see cref="ScrolllerPolicyService.MayFetchFresh"/> says no, or when the user has not
    /// consented to remote media). Null on any failure.
    /// One download per url however many ask; the first asker's token can cancel it, a later
    /// asker only stops waiting.</summary>
    public static Task<string?> GetOrFetchAsync(string url, ScrolllerCacheMeta? meta, CancellationToken ct)
        => GetOrFetchAsync(url, meta, MaxEntryBytes, ct);

    /// <summary><see cref="GetOrFetchAsync(string, ScrolllerCacheMeta?, CancellationToken)"/> for a
    /// consumer with its own size cap: a body the server declares larger is refused on its
    /// Content-Length, before a byte is read or charged.</summary>
    public static async Task<string?> GetOrFetchAsync(string url, ScrolllerCacheMeta? meta, long maxBytes, CancellationToken ct)
    {
        try
        {
            if (!RemoteConsent()) return null;
            await WarmAsync().WaitAsync(ct).ConfigureAwait(false);   // never a cold miss that fetches a cached url
            var hit = TryGetPath(url);
            if (hit != null) return hit;
            if (!global::ConditioningControlPanel.Services.Video.ScrolllerCredit.IsScrolllerUrl(url)
                || !ScrolllerPolicyService.MayFetchFresh(url)) return null;

            Lazy<Task<string?>>? mine = null;
            mine = new Lazy<Task<string?>>(() => Task.Run(async () =>
            {
                try { return await DownloadAsync(url, meta, maxBytes, ct).ConfigureAwait(false); }
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
    /// Empty while the switch is off. A row past the max age is never listed (it is dropped, so a
    /// reuse pick can never replay it).</summary>
    public static IReadOnlyList<ScrolllerCacheEntry> Entries(Func<ScrolllerCacheEntry, bool>? filter = null)
    {
        if (!ScrolllerPolicy.Enabled) return Array.Empty<ScrolllerCacheEntry>();
        try
        {
            List<ScrolllerCacheEntry> all;
            bool dropped = false;
            lock (Gate)
            {
                var book = Book();
                if (book != null)
                    foreach (var e in book.Expired(UtcNow())) dropped |= DropIfExpiredLocked(book, e);
                all = book?.Snapshot() ?? new List<ScrolllerCacheEntry>();
            }
            if (dropped) ScheduleSave();
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

    /// <summary>Writes the index now (app exit). Never throws. A commit after this still saves.</summary>
    public static void Flush()
    {
        lock (SaveLock)
        {
            try { _saveTimer?.Dispose(); } catch { }
            _saveTimer = null;
        }
        Save();
    }

    /// <summary>Loads the books (and sweeps the folder) on a background thread; call at app start.
    /// Until it finishes, every read sees an empty cache: the UI thread never waits on the scan.</summary>
    public static Task WarmAsync()
    {
        try
        {
            lock (Gate) return Book() != null ? Task.CompletedTask : _warm ?? Task.CompletedTask;
        }
        catch { return Task.CompletedTask; }
    }

    /// <summary>Drops every row that is past the max age, or will be before the next sweep, and
    /// deletes its file, then deletes every file in the folder no row names (the index and a part
    /// younger than ten minutes stay). Returns how many went. Never throws. Runs at launch (after
    /// the load) and hourly.</summary>
    public static int SweepExpired()
    {
        int count = SweepOldRoots(offSwitch: false);
        try
        {
            bool dropped = false;
            lock (Gate)
            {
                var book = Book();
                var root = _bookRoot;
                if (book == null || root == null) return 0;
                // One period ahead: what would pass the max age before the next sweep goes now,
                // so no file sits on disk past it waiting for a read that never comes.
                foreach (var e in book.Expired(UtcNow() + SweepPeriod))
                {
                    book.Remove(e.Url);
                    TryDelete(e.Path);
                    dropped = true;
                    count++;
                }
                var known = new HashSet<string>(book.Snapshot().Select(e => Path.GetFileName(e.Path)), StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.GetFiles(root))
                {
                    var name = Path.GetFileName(file);
                    if (known.Contains(name) || IsBookFile(name)) continue;
                    if (name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase) && UtcNow() - File.GetCreationTimeUtc(file) < PartGrace) continue;
                    if (TryDelete(file)) count++;
                }
            }
            if (dropped) ScheduleSave();
            if (count > 0) App.Logger?.Information("[scrolller cache] swept {Count} file(s) past the day limit or unknown", count);
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] sweep failed: {E}", ex.Message); }
        return count;
    }

    /// <summary>The off switch: every row goes, every file in the folder is deleted (a file a
    /// player still holds is left for the next sweep) and the index is written empty. A folder
    /// that does not exist is left alone (never created). Never throws.</summary>
    public static void PurgeAll(string reason)
    {
        var old = SweepOldRoots(offSwitch: true);
        if (old > 0) App.Logger?.Information("[scrolller cache] {Reason}: deleted {Count} file(s) in folders no longer used", reason, old);
        try
        {
            var root = RootProvider();
            if (string.IsNullOrEmpty(root)) return;
            Task? warm;
            lock (Gate) warm = _warm;
            try { warm?.Wait(TimeSpan.FromSeconds(5)); } catch { }   // a load in flight must not reinstall old rows
            int deleted = 0;
            lock (Gate)
            {
                if (_book != null && SameRoot(_bookRoot, root))
                    foreach (var e in _book.Snapshot()) _book.Remove(e.Url);
                if (!Directory.Exists(root)) return;
                foreach (var file in Directory.GetFiles(root))
                {
                    var name = Path.GetFileName(file);
                    if (IsBookFile(name)) continue;
                    if (TryDelete(file)) deleted++;
                }
                WriteIndex(new ScrolllerCacheBook().ToJson(), Path.Combine(root, IndexFileName), Interlocked.Increment(ref _saveSeq));
            }
            if (deleted > 0) App.Logger?.Information("[scrolller cache] {Reason}: deleted {Count} cached file(s)", reason, deleted);
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] purge failed: {E}", ex.Message); }
    }

    /// <summary>Starts the hourly sweep of every Scrolller copy (<see cref="ScrolllerCopySweeper"/>);
    /// its first tick runs as soon as the background load is done. Call once, after
    /// <see cref="WarmAsync"/>. Never throws.</summary>
    public static void StartSweeper()
    {
        try
        {
            lock (SaveLock)
            {
                if (_sweepTimer != null) return;
                _sweepTimer = new Timer(_ => ScrolllerCopySweeper.SweepAll(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            _ = WarmAsync().ContinueWith(_ =>
            {
                lock (SaveLock) { try { _sweepTimer?.Change(TimeSpan.Zero, SweepPeriod); } catch { } }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] sweeper start failed: {E}", ex.Message); }
    }

    // ---- folders no longer used ------------------------------------------------------
    // A new CustomAssetsPath (or a custom drive that fell back to the default) moves the cache.
    // The folder left behind is never read again, so its media and its .temp remote copies are
    // deleted whole, at the move and on every sweep until it is empty (a file a player still
    // holds is retried). Every folder ever used is remembered under the user data folder.

    internal const string RootsFileName = "scrolller-cache-roots.json";

    /// <summary>Where the list of cache folders ever used is kept, or null for none. Replaceable by tests.</summary>
    internal static Func<string?> RootsFileProvider = () =>
    {
        try { return Path.Combine(App.UserDataPath, RootsFileName); }
        catch { return null; }
    };

    /// <summary>Cache folders a build before the list may have used (the default assets folder
    /// and the configured custom one): always swept as if remembered. Replaceable by tests.</summary>
    internal static Func<IEnumerable<string>> SeedRootsProvider = () =>
    {
        var list = new List<string>();
        try { list.Add(Path.Combine(App.UserAssetsPath, FolderName)); } catch { }
        try
        {
            var custom = App.Settings?.Current?.CustomAssetsPath;
            if (!string.IsNullOrWhiteSpace(custom)) list.Add(Path.Combine(custom, FolderName));
        }
        catch { }
        return list;
    };

    private static readonly object RootsLock = new();

    /// <summary>Every cache folder remembered (and the seeds), current one included.</summary>
    internal static IReadOnlyList<string> KnownRoots()
    {
        var all = new List<string>();
        lock (RootsLock) all.AddRange(ReadRoots());
        try { all.AddRange(SeedRootsProvider()); } catch { }
        return all.Where(r => !string.IsNullOrWhiteSpace(r))
                  .Select(NormalRoot)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    private static string NormalRoot(string root)
    {
        try { return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return root; }
    }

    private static List<string> ReadRoots()
    {
        try
        {
            var file = RootsFileProvider();
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return new List<string>();
            return JArray.Parse(File.ReadAllText(file)).Select(t => (string?)t).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        }
        catch { return new List<string>(); }
    }

    private static void WriteRoots(List<string> roots)
    {
        try
        {
            var file = RootsFileProvider();
            if (string.IsNullOrEmpty(file)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, new JArray(roots).ToString(Newtonsoft.Json.Formatting.None));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] roots save failed: {E}", ex.Message); }
    }

    /// <summary>Remembers <paramref name="root"/> as a folder this install used. Never throws.</summary>
    private static void RememberRoot(string root)
    {
        lock (RootsLock)
        {
            var roots = ReadRoots();
            var norm = NormalRoot(root);
            if (roots.Any(r => SameRoot(NormalRoot(r), norm))) return;
            roots.Add(norm);
            WriteRoots(roots);
        }
    }

    private static void ForgetRoot(string root)
    {
        lock (RootsLock)
        {
            var roots = ReadRoots();
            var norm = NormalRoot(root);
            if (roots.RemoveAll(r => SameRoot(NormalRoot(r), norm)) > 0) WriteRoots(roots);
        }
    }

    /// <summary>The <c>.temp</c> folder beside a cache folder (both live in the assets folder).</summary>
    internal static string? TempDirBeside(string root)
    {
        try
        {
            var parent = Path.GetDirectoryName(NormalRoot(root));
            return string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, ".temp");
        }
        catch { return null; }
    }

    /// <summary>Deletes every media file in a folder the cache no longer uses (its books stay,
    /// the index is written empty) and every remote temp copy beside it. Returns how many went
    /// and whether anything is left (a file still open). Never creates a folder. Never throws.</summary>
    internal static (int Deleted, bool Left) PurgeFolder(string root)
    {
        int deleted = 0;
        bool left = false;
        try
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root))
                {
                    if (IsBookFile(Path.GetFileName(file))) continue;
                    if (TryDelete(file)) deleted++; else left = true;
                }
                var index = Path.Combine(root, IndexFileName);
                if (File.Exists(index)) WriteIndex(new ScrolllerCacheBook().ToJson(), index, Interlocked.Increment(ref _saveSeq));
            }
            var temp = TempDirBeside(root);
            if (temp != null && Directory.Exists(temp))
                foreach (var file in Directory.GetFiles(temp, RemoteMediaCache.TempPrefix + "*"))
                    if (TryDelete(file)) deleted++; else left = true;
        }
        catch { left = true; }
        return (deleted, left);
    }

    /// <summary>Purges every known folder that is not the current one (the off switch: every one
    /// when none is current); a folder left empty is forgotten. Runs on every sweep and on the off
    /// switch. Returns how many files went.</summary>
    internal static int SweepOldRoots(bool offSwitch)
    {
        int count = 0;
        try
        {
            string? current = null;
            try { var r = RootProvider(); if (!string.IsNullOrEmpty(r)) current = NormalRoot(r); } catch { }
            // No current folder (none resolvable): an ordinary sweep cannot tell old from live and
            // leaves them; the off switch deletes in every one.
            if (current == null && !offSwitch) return 0;
            foreach (var root in KnownRoots())
            {
                if (current != null && SameRoot(root, current)) continue;
                var (deleted, left) = PurgeFolder(root);
                count += deleted;
                if (!left) ForgetRoot(root);
            }
            if (count > 0) App.Logger?.Information("[scrolller cache] deleted {Count} file(s) in cache folders no longer used", count);
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] old folder sweep failed: {E}", ex.Message); }
        return count;
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

    /// <summary>The folder's own books (the index): never media, never swept as strangers.</summary>
    internal static bool IsBookFile(string name)
        => name.StartsWith(IndexFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Check every body's own bytes before it becomes (or stays) an entry. Tests that
    /// write placeholder bytes switch it off through <see cref="ResetForTests"/>.</summary>
    internal static bool FormatCheck = true;

    private static readonly HashSet<string> VideoExts = new(StringComparer.Ordinal) { ".mp4", ".m4v", ".mov", ".webm", ".mkv" };
    private static readonly HashSet<string> ImageExts = new(StringComparer.Ordinal) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".heic" };

    /// <summary>What the bytes really are, by signature (<see cref="Helpers.MediaTypeSniffer"/>),
    /// or null for anything that is not media (an HTML or XML error page, an empty body).</summary>
    internal static string? SniffFormat(byte[] head, int count)
        => Helpers.MediaTypeSniffer.FromMagicBytes(head, count);

    /// <summary>True when a WEBP head declares animation (VP8X with the ANIM flag).</summary>
    internal static bool IsAnimatedWebp(byte[] head, int count)
        => count >= 21 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
           && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P'
           && head[12] == 'V' && head[13] == 'P' && head[14] == '8' && head[15] == 'X'
           && (head[20] & 0x02) != 0;

    /// <summary>
    /// May a body with this head be kept for a url with extension <paramref name="ext"/> and label
    /// <paramref name="kind"/>? The bytes must be media of the same family as the extension (an S3
    /// "AccessDenied" XML body under a .mp4 url is not), and a "clip" or "gif" label must hold
    /// something that moves: a video, a GIF, or an animated WEBP. A still under a gif label misses
    /// and is fetched again as the clip it should be.
    /// </summary>
    internal static bool BodyFits(string ext, string? kind, byte[] head, int count)
    {
        var real = SniffFormat(head, count) ?? IsoBoxVideo(ext, kind, head, count);
        if (real == null) return false;
        bool video = VideoExts.Contains(real);
        bool image = ImageExts.Contains(real);
        if (kind == ScrolllerReuse.KindGif && video) return true;   // a GIF post served as video, whatever the url says
        if (VideoExts.Contains(ext) && !video) return false;
        if (ImageExts.Contains(ext) && !image) return false;
        switch (kind)
        {
            case ScrolllerReuse.KindClip: return video;
            case ScrolllerReuse.KindGif: return video || real == ".gif" || (real == ".webp" && IsAnimatedWebp(head, count));
            case ScrolllerReuse.KindPicture: case ScrolllerReuse.KindPoster: return image;
            default: return video || image;
        }
    }

    private static readonly string[] IsoBoxes = { "styp", "moof", "sidx", "moov", "mdat", "free", "skip", "wide", "pnot" };

    /// <summary>".mp4" for an ISO-BMFF body that does not open with <c>ftyp</c> (a fragmented
    /// MP4 opens with styp/moof/sidx, a moov-first one with moov), when the url or the label says
    /// video. Null otherwise.</summary>
    internal static string? IsoBoxVideo(string ext, string? kind, byte[] head, int count)
    {
        if (count < 8) return null;
        if (!VideoExts.Contains(ext) && kind != ScrolllerReuse.KindClip && kind != ScrolllerReuse.KindGif) return null;
        foreach (var box in IsoBoxes)
            if (head[4] == box[0] && head[5] == box[1] && head[6] == box[2] && head[7] == box[3]) return ".mp4";
        return null;
    }

    /// <summary>
    /// The startup check, which only drops on POSITIVE evidence (an unknown body is kept: dropping
    /// a good copy would charge the share again for it): a text body (XML, HTML, JSON: an error
    /// page) or a single-frame still under a clip or gif label. Nothing read = kept.
    /// </summary>
    internal static bool KeepAtLoad(string ext, string? kind, byte[] head, int count)
    {
        if (count <= 0) return true;
        var real = SniffFormat(head, count) ?? IsoBoxVideo(ext, kind, head, count);
        if (real == null)
        {
            int i = 0;
            if (count >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) i = 3;   // UTF-8 BOM
            while (i < count && (head[i] == ' ' || head[i] == '\t' || head[i] == '\r' || head[i] == '\n')) i++;
            return !(i < count && (head[i] == '<' || head[i] == '{'));
        }
        if (kind != ScrolllerReuse.KindClip && kind != ScrolllerReuse.KindGif) return true;
        bool still = real is ".jpg" or ".png" or ".bmp" or ".avif" or ".heic"
                     || (real == ".webp" && !IsAnimatedWebp(head, count));
        return !still;
    }

    /// <summary>The first <see cref="Helpers.MediaTypeSniffer.MagicProbeBytes"/> bytes of a file
    /// (fewer when it is shorter, none when it cannot be read).</summary>
    private static (byte[] Head, int Count) ReadHead(string path)
    {
        var buf = new byte[Helpers.MediaTypeSniffer.MagicProbeBytes];
        int read = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            while (read < buf.Length)
            {
                int got = fs.Read(buf, read, buf.Length - read);
                if (got <= 0) break;
                read += got;
            }
        }
        catch { read = 0; }
        return (buf, read);
    }

    /// <summary><see cref="BodyFits"/> (a new body) or <see cref="KeepAtLoad"/> (a stored one) on a
    /// file. Always true while the check is off, and when nothing could be read: only bytes
    /// actually read can condemn a file.</summary>
    private static bool FileFits(string url, string? kind, string path, bool atLoad = false)
    {
        if (!FormatCheck) return true;
        var (head, count) = ReadHead(path);
        if (count == 0) return true;
        var ext = ExtensionOf(url);
        return atLoad ? KeepAtLoad(ext, kind, head, count) : BodyFits(ext, kind, head, count);
    }

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

    private static bool SameRoot(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The book for the current root, or null when there is no folder or it is still
    /// loading (the load runs in the background: never a disk scan on the caller's thread). When
    /// the root moved, the old folder is never read again: its media and its remote temp copies
    /// are deleted now (it stays remembered, so the sweep retries a file a player still holds).
    /// Caller holds <see cref="Gate"/>.</summary>
    private static ScrolllerCacheBook? Book()
    {
        var root = RootProvider();
        if (string.IsNullOrEmpty(root)) return null;
        if (_book != null && SameRoot(root, _bookRoot)) return _book;
        if (_book != null && _bookRoot != null)
        {
            RememberRoot(_bookRoot);
            var (deleted, _) = PurgeFolder(_bookRoot);
            if (deleted > 0) App.Logger?.Information("[scrolller cache] the cache folder moved: deleted {Count} file(s) left behind", deleted);
            _book = null;
            _bookRoot = null;
        }
        if (WarmInline)
        {
            Install(root!, LoadBook(root!));
            return _book;
        }
        StartWarmLocked(root!);
        return null;
    }

    /// <summary>Starts (or joins) the background load of <paramref name="root"/>. A load that
    /// failed is retried at most once a minute. Caller holds <see cref="Gate"/>.</summary>
    private static Task StartWarmLocked(string root)
    {
        if (_warm != null && SameRoot(root, _warmRoot)
            && (!_warm.IsCompleted || UtcNow() - _warmStartedUtc < TimeSpan.FromMinutes(1)))
            return _warm;
        _warmRoot = root;
        _warmStartedUtc = UtcNow();
        _warm = Task.Run(() =>
        {
            try
            {
                var book = LoadBook(root);
                lock (Gate) Install(root, book);
                ScheduleSave();
            }
            catch (Exception ex) { App.Logger?.Debug("[scrolller cache] load failed: {E}", ex.Message); }
        });
        return _warm;
    }

    /// <summary>A loaded book becomes the live one, unless the root moved on meanwhile. Caller
    /// holds <see cref="Gate"/>.</summary>
    private static void Install(string root, ScrolllerCacheBook book)
    {
        if (!SameRoot(RootProvider(), root) || (_book != null && SameRoot(_bookRoot, root))) return;
        _book = book;
        _bookRoot = root;
        EvictLocked(null);
    }

    /// <summary>Reads the index and sweeps the folder. Disk work, no lock held: nothing can write
    /// into <paramref name="root"/> until its book is installed.</summary>
    private static ScrolllerCacheBook LoadBook(string root)
    {
        Directory.CreateDirectory(root);
        RememberRoot(root);
        var indexPath = Path.Combine(root, IndexFileName);
        string? json = null;
        try { if (File.Exists(indexPath)) json = File.ReadAllText(indexPath); } catch { }
        var book = ScrolllerCacheBook.FromJson(json, root);

        // Past the day limit, or from a build that kept no download time: the row goes here and
        // the orphan pass below deletes its file.
        var expired = book.Expired(UtcNow());
        foreach (var e in expired) book.Remove(e.Url);
        if (expired.Count > 0) App.Logger?.Information("[scrolller cache] {Count} cached file(s) past the day limit: deleted", expired.Count);

        // Records whose file is gone, has the wrong size, or whose bytes are not what the url and
        // label say (an error page, a still under a gif label: kept by builds before the format
        // check) go, so each misses once and is fetched again; files the index does not know
        // (a crash mid-download, an eviction that hit an open file, those rows) are deleted.
        // The byte check runs once per index: a stamped one only holds rows the commit check passed.
        bool scan = FormatCheck && !book.FormatChecked;
        int misfits = 0;
        foreach (var e in book.Snapshot())
        {
            try
            {
                var fi = new FileInfo(e.Path);
                if (!fi.Exists || fi.Length != e.Bytes) book.Remove(e.Url);
                else if (scan && !FileFits(e.Url, e.Meta.Kind, e.Path, atLoad: true)) { book.Remove(e.Url); misfits++; }
            }
            catch { book.Remove(e.Url); }
        }
        if (FormatCheck) book.FormatChecked = true;
        if (misfits > 0) App.Logger?.Information("[scrolller cache] dropped {Count} file(s) whose bytes did not match their label", misfits);
        var known = new HashSet<string>(book.Snapshot().Select(e => Path.GetFileName(e.Path)), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.GetFiles(root))
            {
                var name = Path.GetFileName(file);
                if (IsBookFile(name) || known.Contains(name)) continue;
                TryDelete(file);
            }
        }
        catch { }
        return book;
    }

    /// <summary>Drops oldest entries past the cap. Caller holds <see cref="Gate"/>. A file still
    /// open (a player has it) cannot be deleted: its row STAYS (the file is whole and still
    /// serves, so a replay never pays to fetch it again only to fail the rename) and the next
    /// eviction retries it; younger entries go in its place meanwhile.</summary>
    private static void EvictLocked(string? keepUrl)
    {
        if (_book == null) return;
        var stuck = new HashSet<string>(StringComparer.Ordinal);
        long stuckBytes = 0;
        for (int round = 0; round < 8; round++)
        {
            var drops = ScrolllerCacheBook.Evictions(_book.Snapshot().Where(e => !stuck.Contains(e.Url)), CapBytes - stuckBytes, keepUrl);
            if (drops.Count == 0) return;
            bool anyStuck = false;
            foreach (var e in drops)
            {
                if (TryDelete(e.Path)) { _book.Remove(e.Url); continue; }
                stuck.Add(e.Url);
                stuckBytes += e.Bytes;
                anyStuck = true;
            }
            if (!anyStuck) return;
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
            // Stored as served, never re-encoded: so the bytes themselves must be the media the
            // url names. The download is still charged (it came); it just never replays.
            if (!FileFits(url, meta.Kind, part))
            {
                TryDelete(part);
                App.Logger?.Debug("[scrolller cache] body from {Host} is not the {Kind} its url names: not kept", Logging.UrlLog.Host(url), meta.Kind);
                return null;
            }
            string final;
            lock (Gate)
            {
                var book = Book();
                // The switch went off mid-download: the purge already ran, so nothing new is kept.
                if (book == null || _bookRoot == null || !ScrolllerPolicy.Enabled) { TryDelete(part); return null; }
                final = Path.Combine(_bookRoot, FileNameFor(url));
                try { File.Move(part, final, overwrite: true); }
                catch
                {
                    // The same url is already cached and open in a player: keep that copy.
                    TryDelete(part);
                    var existing = book.Get(url);
                    if (existing != null && !DropIfExpiredLocked(book, existing) && File.Exists(existing.Path))
                    {
                        book.Touch(url, UtcNow());
                        return existing.Path;
                    }
                    return null;
                }
                var now = UtcNow();
                book.Put(new ScrolllerCacheEntry(url, final, len, now, meta, DownloadedUtc: now));
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

    /// <summary>One fresh download into the cache. <paramref name="maxBytes"/> (never above
    /// <see cref="MaxEntryBytes"/>) is checked on the declared length BEFORE any body byte is read
    /// or charged, and again as the body flows (a chunked body has no length up front).
    /// <paramref name="http"/> is for tests.</summary>
    internal static async Task<string?> DownloadAsync(string url, ScrolllerCacheMeta? meta, long maxBytes, CancellationToken ct, HttpClient? http = null)
    {
        long uncharged = 0;
        long cap = Math.Min(maxBytes, MaxEntryBytes);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DownloadBudget);
            using var resp = await (http ?? Http.Value).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                App.Logger?.Debug("[scrolller cache] HTTP {Code} for {Host}", (int)resp.StatusCode, Logging.UrlLog.Host(url));
                return null;
            }
            long? declared = resp.Content.Headers.ContentLength;
            if (declared > cap)
            {
                App.Logger?.Debug("[scrolller cache] item from {Host} is {Size} bytes, over the {Cap} cap: skipped unread",
                    Logging.UrlLog.Host(url), declared, cap);
                return null;
            }

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
                // Per-account share: each step goes to whoever is signed in when it arrives, so a
                // download spanning a sign-in or sign-out splits (bounded by the cap above).
                if (uncharged >= ChargeStepBytes) { ScrolllerAllowance.Charge(uncharged); uncharged = 0; }
                if (got > cap) return null;
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
            if (uncharged > 0) ScrolllerAllowance.Charge(uncharged); // the account signed in now
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
        lock (SaveLock)
        {
            try
            {
                if (_saveTimer != null) return;
                _saveTimer = new Timer(_ => OnSaveTimer(), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            }
            catch { }
        }
    }

    private static void OnSaveTimer()
    {
        // Drop the timer BEFORE saving: a commit that lands during the write schedules a new one.
        lock (SaveLock)
        {
            try { _saveTimer?.Dispose(); } catch { }
            _saveTimer = null;
        }
        Save();
    }

    private static void Save()
    {
        try
        {
            string json, path;
            long seq;
            lock (Gate)
            {
                if (_book == null || _bookRoot == null) return;
                json = _book.ToJson();
                path = Path.Combine(_bookRoot, IndexFileName);
                seq = Interlocked.Increment(ref _saveSeq);
            }
            WriteIndex(json, path, seq);
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] index save failed: {E}", ex.Message); }
    }

    /// <summary>One writer of the .tmp at a time; a snapshot older than the last one written is
    /// dropped. May be called with <see cref="Gate"/> held (never the other way round).</summary>
    private static void WriteIndex(string json, string path, long seq)
    {
        try
        {
            lock (FileLock)
            {
                if (seq < _writtenSeq) return;
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
                _writtenSeq = seq;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[scrolller cache] index save failed: {E}", ex.Message); }
    }

    /// <summary>Deletes the file. True when it is gone (or never was).</summary>
    private static bool TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        try { if (File.Exists(path)) File.Delete(path); return true; } catch { return false; }
    }

    /// <summary>Tests only: forget the books (the folder stays); loads run inline.</summary>
    internal static void ResetForTests()
    {
        lock (SaveLock)
        {
            try { _saveTimer?.Dispose(); } catch { }
            _saveTimer = null;
        }
        lock (Gate) { _book = null; _bookRoot = null; _warm = null; _warmRoot = null; }
        InFlight.Clear();
        CapBytes = DefaultCapBytes;
        MaxAge = DefaultMaxAge;
        WarmInline = true;
        FormatCheck = false;
        RemoteConsent = () => true;
    }
}
