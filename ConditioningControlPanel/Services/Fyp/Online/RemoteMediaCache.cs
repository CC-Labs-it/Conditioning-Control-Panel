using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// The one place remote media bytes are fetched, held and handed to a consumer. Two shapes,
/// because the app's asset consumers split cleanly in two (planning/remote-media/PLAN.md §2):
///
///   * <see cref="OpenAsync"/> — a <see cref="Stream"/> over in-memory bytes, for the
///     Contract-2 surfaces (<c>BitmapImage.StreamSource</c>, SKCodec). No disk at all.
///   * <see cref="MaterializeAsync"/> — a real path on local disk, for the Contract-1
///     surfaces that can only take one (LibVLC <c>FromPath</c>, <c>SPI_SETDESKWALLPAPER</c>,
///     <c>System.Drawing.Image.FromFile</c>).
///
/// THE BRIGHT LINE (<see cref="IFeedSource"/>): every byte here travels provider → this
/// machine. Nothing routes through, is cached on, or is re-served by CC Labs infrastructure.
/// The transient temp files below are on the USER'S own disk, which the owner explicitly
/// cleared on 2026-08-12 (decision 7) — that line is about our servers, not their drive.
///
/// TEMP FILES DELIBERATELY MIRROR THE CONTENT-PACK PATTERN
/// (<c>ContentPackService.GetPackFileTempPath</c>): same directory (<c>App.GetMediaTempPath()</c>,
/// i.e. <c>{EffectiveAssetsPath}/.temp</c>, so temp lives on the same drive as the library),
/// same <c>ccp_temp_</c> filename prefix, same "track them and clean up above N" rule, same
/// startup sweep. The prefix matters twice over: <c>App.CleanupStaleTempFiles</c> globs
/// <c>ccp_temp_*</c>, so a crash that skips every path below is still recovered on the next
/// launch, while the extra <c>remote_</c> segment lets <see cref="CleanupStaleTempFiles"/>
/// sweep only ours. A second temp scheme would have been a second thing to leak.
///
/// NOTHING HERE THROWS. A dead CDN, a DNS failure, a full disk and a cancelled token all
/// come back as null, because every caller's fallback is "no media" and a flash pipeline
/// that faults on a bad hotspot is worse than one that shows a local image instead.
/// </summary>
internal static class RemoteMediaCache
{
    // ---- bounds --------------------------------------------------------------------
    //
    // These hold COMPRESSED source bytes, not decoded pixels — a 1080p still is ~200-600KB
    // here versus ~8MB once WIC has it, and FlashService's own decode cache (50 entries /
    // 200MB) is what actually holds the expensive form. This cache only has to bridge
    // "prefetched" to "decoded", so it is deliberately an order of magnitude cheaper.
    // Both caps are enforced: a feed of stills would blow the entry count long before the
    // byte count, and one stray 4K png would do the reverse.

    private const int MaxEntries = 64;
    private const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>Anything larger is refused outright rather than evicting the whole cache to
    /// hold it. Comfortably above any still (<see cref="ScrolllerSource"/> caps the source
    /// pick at 1920px) and above a short feed clip.</summary>
    private const long MaxSingleEntryBytes = 24L * 1024 * 1024;

    /// <summary>Tracked temp files above which the oldest get swept, matching the
    /// content-pack convention (<c>FlashService.GetNextImages</c> cleans above 50).</summary>
    private const int MaxTrackedTempFiles = 50;

    /// <summary>Filename prefix. The <c>ccp_temp_</c> half is load-bearing — see the class
    /// remarks — and <c>remote_</c> is what makes our sweep ours alone.</summary>
    private const string TempPrefix = "ccp_temp_remote_";

    private const int CopyBufferBytes = 81920;

    // ---- state ---------------------------------------------------------------------

    private sealed class CacheEntry
    {
        public byte[] Bytes = Array.Empty<byte>();
        public long Stamp;          // LRU: last touched, monotonically increasing
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    /// <summary>Single-flight: one download per URL no matter how many callers ask. A burst
    /// of flashes drawing from the same prefetched batch would otherwise fetch the same
    /// image once per flash.</summary>
    private static readonly Dictionary<string, Task<byte[]?>> InFlight = new(StringComparer.Ordinal);

    /// <summary>A refused delete (a clip LibVLC still has open) stays on these books and is
    /// retried on the next sweep; it used to be dropped and left on disk until the next launch.</summary>
    private static readonly MediaTempFileLedger Temps = new(MaxTrackedTempFiles);

    private static long _bytes;
    private static long _tick;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The same honest identity <see cref="ScrolllerSource"/> uses for the API: the
    /// app's own user-agent and no Referer.</summary>
    internal static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ScrolllerSource.HonestUserAgent);
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/webp,image/jpeg,image/png,video/*,*/*;q=0.8");
        return c;
    }

    // ---- public surface ------------------------------------------------------------

    /// <summary>
    /// In-memory bytes for Contract-2 consumers (<c>BitmapImage.StreamSource</c>, SKCodec).
    /// Null when the URL is not usable remote media, the fetch failed, or the token fired.
    ///
    /// Each caller gets its OWN <see cref="MemoryStream"/> over the shared (immutable) byte
    /// array — a single shared stream would have concurrent decoders fighting over one
    /// Position. The caller owns and disposes what it gets back; the bytes stay cached.
    /// </summary>
    public static async Task<Stream?> OpenAsync(string url, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(url, ct).ConfigureAwait(false);
        if (bytes == null) return null;
        try
        {
            // writable:false — nobody may mutate the array the cache is still handing out.
            return new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[remote media] stream open failed for {Host}: {E}", Logging.UrlLog.Host(url), ex.Message);
            return null;
        }
    }

    /// <summary>
    /// A real file on local disk for Contract-1 consumers (LibVLC <c>FromPath</c>, wallpaper).
    /// Null on any failure. The file carries the source's extension because several consumers
    /// sniff the codec from it.
    ///
    /// The caller SHOULD hand the path back to <see cref="ReleaseTempFile"/> when it is done;
    /// if it forgets (or crashes), the cleanup-above-N rule and the startup sweep both still
    /// catch the file. A fresh file per call, exactly like the content-pack path — sharing one
    /// file between two players is how you get a locked handle mid-playback.
    ///
    /// <paramref name="ownerReleases"/>: the caller holds the file for a long time (a whole game's
    /// picture pool) and ALWAYS releases it itself. Such a file stays out of the cleanup-above-N
    /// list, so a burst of flashes elsewhere in the app cannot delete a picture a game is still
    /// showing. The startup sweep still catches it after a crash.
    /// </summary>
    public static Task<string?> MaterializeAsync(string url, CancellationToken ct) => MaterializeAsync(url, ct, ownerReleases: false);

    public static async Task<string?> MaterializeAsync(string url, CancellationToken ct, bool ownerReleases)
    {
        var bytes = await GetBytesAsync(url, ct).ConfigureAwait(false);
        if (bytes == null) return null;

        string? path = null;
        try
        {
            // URL first (it is what the consumers' own format checks were made against), then
            // the bytes' own signature. ".bin" is gone on purpose: a file with an extension no
            // player recognises is the same bug as a file with none — see MediaTypeSniffer.
            var ext = ExtensionOf(url)
                      ?? Helpers.MediaTypeSniffer.FromMagicBytes(bytes, Helpers.MediaTypeSniffer.MagicProbeBytes)
                      ?? Helpers.MediaTypeSniffer.DefaultImageExtension;
            path = Path.Combine(App.GetMediaTempPath(), $"{TempPrefix}{Guid.NewGuid():N}{ext}");
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A half-written file is worse than none: a video player would open it and stall.
            TryDelete(path);
            App.Logger?.Warning("[remote media] temp materialize failed for {Host}: {E}", Logging.UrlLog.Host(url), ex.Message);
            return null;
        }

        if (!ownerReleases) TrackTempFile(path);
        return path;
    }

    /// <summary>
    /// Consumer is done with a materialized file. Ignores null, unknown and non-ours paths, and
    /// never throws — a locked file (the player has not let go yet) is simply left for the
    /// startup sweep rather than retried on a timer.
    /// </summary>
    public static void ReleaseTempFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // Only ever delete something we made. A caller that mixes a library path into its
        // release bookkeeping must not be able to delete the user's own file through us.
        if (!IsOurTempFile(path)) return;

        Temps.Release(path);
    }

    /// <summary>
    /// Startup sweep for temp files a crash left behind. Called from <c>App.OnStartup</c>
    /// beside <c>App.CleanupStaleTempFiles</c> — that one already globs <c>ccp_temp_*</c> and
    /// therefore covers ours, so this is belt to those braces AND the only sweep that can run
    /// again later in the session without touching content-pack temps.
    /// </summary>
    public static void CleanupStaleTempFiles()
    {
        int deleted = 0;
        try
        {
            var dir = App.GetMediaTempPath();
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, TempPrefix + "*"))
            {
                try { File.Delete(file); deleted++; }
                catch { /* still in use: App.CleanupStaleTempFiles gets it next launch */ }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[remote media] stale temp sweep failed: {E}", ex.Message);
        }

        if (deleted > 0)
            App.Logger?.Information("[remote media] swept {Count} stale temp file(s)", deleted);
    }

    /// <summary>
    /// True when the bytes are already in memory, so a consumer on the UI thread can pick this
    /// URL knowing the decode will not wait on a network round trip. Prefetchers use this to
    /// decide what is safe to put in a pool.
    /// </summary>
    public static bool IsCached(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        lock (Gate) return Cache.ContainsKey(url);
    }

    /// <summary>
    /// Warm one URL without allocating a stream for it. Returns true when the bytes are
    /// resident afterwards. This is the call a prefetcher makes; every consumer path stays
    /// synchronous-feeling because of it.
    /// </summary>
    public static async Task<bool> PrefetchAsync(string url, CancellationToken ct)
        => await GetBytesAsync(url, ct).ConfigureAwait(false) != null;

    /// <summary>Drop every cached byte (memory pressure, session teardown). Temp files are
    /// untouched — someone may still be playing one.</summary>
    public static void ClearMemory()
    {
        lock (Gate)
        {
            Cache.Clear();
            _bytes = 0;
        }
    }

    // ---- fetch ---------------------------------------------------------------------

    /// <summary>Cache hit, join an in-flight download, or start one. Never throws.</summary>
    private static async Task<byte[]?> GetBytesAsync(string url, CancellationToken ct)
    {
        if (!IsUsable(url))
        {
            App.Logger?.Debug("[remote media] refused non-media url from {Host}", Logging.UrlLog.Host(url));
            return null;
        }
        // Licence switch off: a Scrolller url reads as a failed fetch (cache hits too, so
        // nothing of theirs keeps showing). Same null every caller already handles.
        if (ScrolllerPolicyService.Blocks(url)) return null;

        try
        {
            var task = GetOrStart(url);
            // The shared download is NOT bound to this caller's token — a second consumer
            // may still want the bytes after the first gives up, and a cancelled prefetch
            // that poisoned the download for everyone else would be a nasty intermittent.
            // WaitAsync detaches THIS caller instead, leaving the fetch to finish and cache.
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[remote media] fetch from {Host} failed: {E}", Logging.UrlLog.Host(url), ex.Message);
            return null;
        }
    }

    private static Task<byte[]?> GetOrStart(string url)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(url, out var hit))
            {
                hit.Stamp = ++_tick;
                return Task.FromResult<byte[]?>(hit.Bytes);
            }
            if (InFlight.TryGetValue(url, out var running)) return running;

            // Task.Run so the synchronous head of DownloadAsync never runs under Gate.
            var started = Task.Run(() => DownloadAsync(url));
            InFlight[url] = started;
            return started;
        }
    }

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            // Scrolller media goes through the device cache (owner, 2026-10-19): a cached copy
            // costs no network and nothing against the daily share, a fresh one is charged.
            if (global::ConditioningControlPanel.Services.Video.ScrolllerCredit.IsScrolllerUrl(url))
            {
                var viaDisk = await FromScrolllerCacheAsync(url).ConfigureAwait(false);
                if (viaDisk != null) Store(url, viaDisk);
                return viaDisk;
            }

            // ResponseHeadersRead so an oversized body is refused on Content-Length rather
            // than after we have already buffered all of it.
            using var resp = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                App.Logger?.Debug("[remote media] HTTP {Code} for {Host}", (int)resp.StatusCode, Logging.UrlLog.Host(url));
                return null;
            }

            long declared = resp.Content.Headers.ContentLength ?? -1;
            if (declared > MaxSingleEntryBytes)
            {
                App.Logger?.Debug("[remote media] item from {Host} is {Size} bytes — over the per-item cap, skipped",
                    Logging.UrlLog.Host(url), declared);
                return null;
            }

            var bytes = await ReadBoundedAsync(resp, url).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return null;

            Store(url, bytes);
            return bytes;
        }
        catch (Exception ex)
        {
            // Includes the HttpClient timeout (surfaces as TaskCanceledException) and every
            // transport failure. One log line at Debug: a flaky CDN must not spam the log.
            App.Logger?.Debug("[remote media] download from {Host} failed: {E}", Logging.UrlLog.Host(url), ex.Message);
            return null;
        }
        finally
        {
            lock (Gate) InFlight.Remove(url);
        }
    }

    /// <summary>How a url that missed the in-memory layer is served.</summary>
    internal enum Route { Network, DiskHit, FreshThroughDisk, Refused }

    /// <summary>The routing rule, pure: a non-Scrolller url goes to the network as it always did;
    /// a Scrolller url is read from the device cache when it is there, else fetched INTO it when a
    /// fresh fetch is allowed (switch on, share left, someone watching), else refused (null, and
    /// every caller already falls back to local media).</summary>
    internal static Route RouteFor(bool isScrolller, bool diskHit, bool mayFetchFresh)
    {
        if (!isScrolller) return Route.Network;
        if (diskHit) return Route.DiskHit;
        return mayFetchFresh ? Route.FreshThroughDisk : Route.Refused;
    }

    private static async Task<byte[]?> FromScrolllerCacheAsync(string url)
    {
        var path = ScrolllerDiskCache.TryGetPath(url);
        switch (RouteFor(true, path != null, ScrolllerPolicyService.MayFetchFresh(url)))
        {
            case Route.DiskHit: break;
            case Route.FreshThroughDisk:
                // This cache's own per-item cap goes along, so an oversized body is refused on
                // its Content-Length before a byte is read or charged (as the plain path does).
                path = await ScrolllerDiskCache.GetOrFetchAsync(url, null, MaxSingleEntryBytes, CancellationToken.None).ConfigureAwait(false);
                break;
            default:
                return null;
        }
        if (path == null) return null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxSingleEntryBytes) return null;
        return await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Body → bytes, abandoning anything that grows past the per-item cap mid-stream
    /// (a chunked response has no Content-Length to check up front).</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage resp, string url)
    {
        using var src = await resp.Content.ReadAsStreamAsync(CancellationToken.None).ConfigureAwait(false);
        long declared = resp.Content.Headers.ContentLength ?? -1;
        // Right-sized up front when the server told us; grown on demand when it didn't.
        using var buffer = declared > 0 && declared <= MaxSingleEntryBytes
            ? new MemoryStream((int)declared)
            : new MemoryStream();

        var chunk = new byte[CopyBufferBytes];
        while (true)
        {
            int read = await src.ReadAsync(chunk.AsMemory(0, chunk.Length), CancellationToken.None).ConfigureAwait(false);
            if (read <= 0) break;
            if (buffer.Length + read > MaxSingleEntryBytes)
            {
                App.Logger?.Debug("[remote media] item from {Host} exceeded the per-item cap mid-stream, dropped", Logging.UrlLog.Host(url));
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Insert and evict least-recently-used until both caps hold.</summary>
    private static void Store(string url, byte[] bytes)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(url, out var existing))
            {
                // A racing duplicate download: release the old accounting before replacing,
                // or _bytes drifts upward forever and the cache silently shrinks to nothing.
                _bytes -= existing.Bytes.LongLength;
                Cache.Remove(url);
            }

            while (Cache.Count > 0 && (Cache.Count + 1 > MaxEntries || _bytes + bytes.LongLength > MaxBytes))
            {
                string? oldest = null;
                long oldestStamp = long.MaxValue;
                foreach (var kvp in Cache)
                {
                    if (kvp.Value.Stamp < oldestStamp) { oldestStamp = kvp.Value.Stamp; oldest = kvp.Key; }
                }
                if (oldest == null) break;
                _bytes -= Cache[oldest].Bytes.LongLength;
                Cache.Remove(oldest);
            }

            Cache[url] = new CacheEntry { Bytes = bytes, Stamp = ++_tick };
            _bytes += bytes.LongLength;
        }
    }

    // ---- temp bookkeeping ----------------------------------------------------------

    // Oldest-first, never the ones just handed out: those are the likeliest to be open in a
    // player right now. A delete that is refused is HELD and retried on the next sweep
    // (ccp-bugs #1433 review): dropping it left every clip that was still on screen at sweep
    // time on disk for the rest of the session.
    private static void TrackTempFile(string path) => Temps.Track(path);

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { App.Logger?.Debug("[remote media] temp delete failed: {E}", ex.Message); }
    }

    private static bool IsOurTempFile(string path)
    {
        try { return Path.GetFileName(path).StartsWith(TempPrefix, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    // ---- url validation ------------------------------------------------------------

    /// <summary>
    /// Gate every fetch through <see cref="RemoteMediaFormats"/> so this cache can never be
    /// turned into a general-purpose downloader by a caller that hands it an arbitrary URL.
    /// Absolute http(s) only, and only the containers the app can actually render.
    /// </summary>
    private static bool IsUsable(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return RemoteMediaFormats.IsRemoteImage(url) || RemoteMediaFormats.IsRemoteVideo(url);
    }

    /// <summary>The source extension, query/fragment stripped, or null. Consumers that sniff
    /// a codec from the filename need this to survive the trip through temp.</summary>
    private static string? ExtensionOf(string url)
    {
        try
        {
            int cut = url.IndexOfAny(new[] { '?', '#' });
            var clean = cut >= 0 ? url[..cut] : url;
            var ext = Path.GetExtension(clean);
            return string.IsNullOrEmpty(ext) ? null : ext.ToLowerInvariant();
        }
        catch { return null; }
    }
}
