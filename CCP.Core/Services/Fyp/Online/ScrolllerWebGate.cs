using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>What the gate does with one WebView2 request.</summary>
internal enum ScrolllerWebDecision
{
    /// <summary>Not a Scrolller host: left alone.</summary>
    Pass,
    /// <summary>Scrolller host while the licence switch is off: answered locally with an empty 403.</summary>
    Block,
    /// <summary>Scrolller host while on: goes out with the app's honest user-agent.</summary>
    Rewrite,
}

/// <summary>What a Scrolller media request is, from its url (or the page's resource context).</summary>
internal enum ScrolllerMediaKind { Other, Picture, Gif, Clip }

/// <summary>Where one Scrolller request goes once the switch is on (see <see cref="ScrolllerWebGate.Route"/>).</summary>
internal enum ScrolllerWebRoute
{
    /// <summary>Not Scrolller: left alone.</summary>
    Pass,
    /// <summary>Switch off: local 403.</summary>
    Block,
    /// <summary>API calls and anything that is not media: honest UA, not charged.</summary>
    Rewrite,
    /// <summary>Media, not cached, and no fresh fetch allowed (share used up, or away): local 403.</summary>
    Refuse,
    /// <summary>Media with a complete cached copy: answered from the file, zero network, zero charge.</summary>
    ServeCached,
    /// <summary>Picture or GIF: the app fetches it whole, tees it into the cache, charges, then serves it.</summary>
    FetchWhole,
    /// <summary>Clip or ranged request: goes out (honest UA) and is charged by the response's length.</summary>
    PassCharged,
}

/// <summary>A byte range of a file, both ends inclusive.</summary>
internal readonly record struct ScrolllerByteRange(long Start, long End)
{
    public long Length => End - Start + 1;
}

/// <summary>
/// The pages (Goon, DtRH, Arcademy, Intake, chess, Back Room, For You, the players...) fetch
/// Scrolller from inside WebView2, so without this every request wears the Edge user-agent.
/// <see cref="Attach"/> hooks one CoreWebView2: a Scrolller request goes out as
/// <see cref="ScrolllerSource.HonestUserAgent"/>, or never leaves the machine while
/// <see cref="ScrolllerPolicy.Enabled"/> is off (owner, 2026-10-18, Scrolller licence).
/// Probed on WebView2 runtime 155 (SDK 1.0.2535.41): a User-Agent set here reaches the server for
/// images, fetch, media and worker fetches; RemoveHeader("Referer") is ignored but SetHeader
/// overwrites it, so a foreign Referer is replaced with ours.
///
/// Daily share (owner, 2026-10-19): media with a cached copy (<see cref="ScrolllerDiskCache"/>) is
/// answered from the file (Range gets a 206); media with no copy and no fresh fetch allowed
/// (<see cref="ScrolllerPolicyService.MayFetchFresh"/>) gets a local 403 and the page falls back.
/// Probed 2026-10-19 on runtime 155: WebView2 reads a managed response stream on a worker thread
/// but holds the page's response (headers included) until the stream ENDS, so a clip cannot be
/// streamed through here. So pictures and GIFs are fetched whole by the app (teed into the cache,
/// charged as bytes arrive), clips and ranged requests go out as before, and a cached clip is
/// answered in capped 206 slices (a video loads, seeks and plays to the end on them, same probe).
/// A passed-through response is charged by the bytes that really arrived, read from the DevTools
/// Network domain on the same core (<see cref="ScrolllerRealBytesMeter"/>); a core where
/// Network.enable fails falls back to the response's declared length.
/// </summary>
internal static partial class ScrolllerWebGate
{
    internal const string OurReferer = "https://cclabs.app/";

    /// <summary>Marks a response the gate made itself, so the response hook never charges it.</summary>
    internal const string MarkerHeader = "X-CCP-Gate";

    /// <summary>An open-ended Range (<c>bytes=N-</c>) on a cached file gets at most this much.</summary>
    internal const long SliceBytes = 2L * 1024 * 1024;

    /// <summary>Largest body the gate holds in memory (an explicit range, or a whole fetch).</summary>
    internal const long MaxBodyBytes = 64L * 1024 * 1024;

    internal const int ChargeBatchBytes = 256 * 1024;

    /// <summary>A whole fetch gives up when no byte arrives for this long...</summary>
    internal static readonly TimeSpan FetchIdleTimeout = TimeSpan.FromSeconds(20);
    /// <summary>...or when the whole fetch, headers and body, takes longer than this.</summary>
    internal static readonly TimeSpan FetchTotalTimeout = TimeSpan.FromSeconds(120);

    /// <summary>A passed-on request whose response never came (cancelled first) is forgotten after
    /// this; an overfull book only ever drops marks this old, never one still in flight.</summary>
    internal const long PassedTtlMs = 10 * 60 * 1000;
    private const int PassedSoftCap = 2000;

    /// <summary>Network.enable without body buffering: the gate only reads sizes, so DevTools must
    /// not keep copies of response bodies in the renderer.</summary>
    internal const string NetworkEnableParams = "{\"maxTotalBufferSize\":0,\"maxResourceBufferSize\":0,\"maxPostDataSize\":0}";

    private sealed class CoreState
    {
        /// <summary>True once Network.enable succeeded: the DevTools meter charges this core.</summary>
        public volatile bool RealBytes;
        public readonly ScrolllerRealBytesMeter Meter = new(ChargeCapped);
    }

    private static readonly ConditionalWeakTable<CoreWebView2, CoreState> Attached = new();
    /// <summary>Urls the gate passed on to the network and not yet charged: count + last mark time.</summary>
    private static readonly ConcurrentDictionary<string, (int Count, long Ticks)> Passed = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<Fetched?>> InFlight = new();
    /// <summary>url -> cached path (or null) for a short while, so a clip's 2 MB slices do not
    /// each take the cache lock and hit the disk on the UI thread.</summary>
    private static readonly ConcurrentDictionary<string, (string? Path, long Ticks)> PathMemo = new(StringComparer.Ordinal);
    private const long PathMemoHitMs = 30_000, PathMemoMissMs = 5_000;
    private static readonly HttpClient Http = CreateHttp();

    private sealed record Fetched(int Status, string Reason, byte[] Body, int Length, string ContentType);

    /// <summary>scrolller.com or any subdomain of it; never a lookalike such as notscrolller.com.</summary>
    internal static bool IsScrolllerHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var h = host.TrimEnd('.');
        return h.Equals("scrolller.com", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".scrolller.com", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsApiHost(string? host)
        => host != null && host.TrimEnd('.').Equals("api.scrolller.com", StringComparison.OrdinalIgnoreCase);

    internal static ScrolllerWebDecision Decide(string? requestUri, bool enabled)
    {
        if (string.IsNullOrEmpty(requestUri)) return ScrolllerWebDecision.Pass;
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)) return ScrolllerWebDecision.Pass;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != "wss" && uri.Scheme != "ws") return ScrolllerWebDecision.Pass;
        if (!IsScrolllerHost(uri.Host)) return ScrolllerWebDecision.Pass;
        return enabled ? ScrolllerWebDecision.Rewrite : ScrolllerWebDecision.Block;
    }

    /// <summary>Kind from the url's extension; an unknown extension falls back to the page's
    /// resource context (a &lt;video&gt; is a clip, an &lt;img&gt; a picture).</summary>
    internal static ScrolllerMediaKind KindOf(string? requestUri, bool mediaContext = false, bool imageContext = false)
    {
        switch (ExtensionOf(requestUri))
        {
            case ".mp4": case ".webm": case ".m4v": case ".mov": return ScrolllerMediaKind.Clip;
            case ".gif": return ScrolllerMediaKind.Gif;
            case ".webp": case ".jpg": case ".jpeg": case ".png": case ".avif": return ScrolllerMediaKind.Picture;
        }
        if (mediaContext) return ScrolllerMediaKind.Clip;
        if (imageContext) return ScrolllerMediaKind.Picture;
        return ScrolllerMediaKind.Other;
    }

    /// <summary>The whole routing rule, pure. Cached beats everything (free); then the share;
    /// then clips and ranged requests go out charged, pictures and GIFs are fetched whole.</summary>
    internal static ScrolllerWebRoute Route(string? requestUri, string? method, bool enabled, bool cached,
        bool mayFetchFresh, bool hasRange, ScrolllerMediaKind kind)
    {
        switch (Decide(requestUri, enabled))
        {
            case ScrolllerWebDecision.Pass: return ScrolllerWebRoute.Pass;
            case ScrolllerWebDecision.Block: return ScrolllerWebRoute.Block;
        }
        var uri = new Uri(requestUri!);
        if (!string.Equals(method ?? "GET", "GET", StringComparison.OrdinalIgnoreCase)
            || IsApiHost(uri.Host) || kind == ScrolllerMediaKind.Other
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return ScrolllerWebRoute.Rewrite;
        if (cached) return ScrolllerWebRoute.ServeCached;
        if (!mayFetchFresh) return ScrolllerWebRoute.Refuse;
        if (hasRange || kind == ScrolllerMediaKind.Clip) return ScrolllerWebRoute.PassCharged;
        return ScrolllerWebRoute.FetchWhole;
    }

    /// <summary>
    /// Parses one <c>Range: bytes=...</c> against a file of <paramref name="total"/> bytes.
    /// False = serve the whole file with a 200 (no header, junk, several ranges). True with
    /// <paramref name="unsatisfiable"/> = answer 416. An open end is capped at
    /// <see cref="SliceBytes"/> (the player asks for the next slice), an explicit one at
    /// <see cref="MaxBodyBytes"/>.
    /// </summary>
    internal static bool TryParseRange(string? header, long total, out ScrolllerByteRange range, out bool unsatisfiable)
    {
        range = default;
        unsatisfiable = false;
        if (string.IsNullOrWhiteSpace(header) || total <= 0) return false;
        var h = header.Trim();
        if (!h.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var spec = h.Substring(6).Trim();
        if (spec.Contains(',')) return false;
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;
        var a = spec.Substring(0, dash).Trim();
        var b = spec.Substring(dash + 1).Trim();
        long start, end;
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var suffix) || suffix < 0) return false;
            if (suffix == 0) { unsatisfiable = true; return true; }
            start = Math.Max(0, total - suffix);
            end = total - 1;
            if (end - start + 1 > MaxBodyBytes) start = end - MaxBodyBytes + 1;
        }
        else
        {
            if (!long.TryParse(a, out start) || start < 0) return false;
            if (start >= total) { unsatisfiable = true; return true; }
            if (b.Length == 0) end = Math.Min(total - 1, start + SliceBytes - 1);
            else
            {
                if (!long.TryParse(b, out end) || end < start) return false;
                end = Math.Min(Math.Min(end, total - 1), start + MaxBodyBytes - 1);
            }
        }
        range = new ScrolllerByteRange(start, end);
        return true;
    }

    internal static string ContentTypeFor(string? requestUri) => ExtensionOf(requestUri) switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".avif" => "image/avif",
        _ => "application/octet-stream",
    };

    /// <summary>Headers for a response the gate answers itself. Pages load Scrolller cross-origin
    /// (ccp.game, fetch and WebGL textures), so it allows any origin as the CDN does. No-store:
    /// the device cache already holds these bytes under the day limit, and Chromium must not keep
    /// a second copy of its own (licence 3.4).</summary>
    internal static string ResponseHeaders(string contentType, long length, ScrolllerByteRange? range = null, long total = 0)
    {
        var s = $"Content-Type: {contentType}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\n";
        if (range is ScrolllerByteRange r) s += $"Content-Range: bytes {r.Start}-{r.End}/{total}\r\n";
        return s + $"Cache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\n{MarkerHeader}: local";
    }

    /// <summary>Headers for the gate's own refusals (403, 416, 502): any origin may read them, so a
    /// page's fetch sees the status instead of a CORS error, and the marker keeps them uncharged.</summary>
    internal static string LocalErrorHeaders(string? extra = null)
        => (string.IsNullOrEmpty(extra) ? "" : extra + "\r\n") + $"Access-Control-Allow-Origin: *\r\n{MarkerHeader}: local";

    /// <summary>Bytes a passed-through response carried: Content-Length, else the Content-Range
    /// span, else 0 (not charged; a chunked body tells us nothing up front).</summary>
    internal static long ChargeableLength(string? contentLength, string? contentRange)
    {
        if (long.TryParse(contentLength?.Trim(), out var n) && n > 0) return n;
        var cr = contentRange?.Trim();
        if (cr == null || !cr.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase)) return 0;
        var span = cr.Substring(6).Split('/')[0].Split('-');
        if (span.Length == 2 && long.TryParse(span[0], out var a) && long.TryParse(span[1], out var b) && b >= a)
            return b - a + 1;
        return 0;
    }

    /// <summary>A Referer is ours when it names cclabs.app (or a subdomain) or one of the app's own
    /// virtual hosts (ccp.game, ccp.assets, leash-video.ccp...). Missing counts as ours.</summary>
    internal static bool IsOurReferer(string? referer)
    {
        if (string.IsNullOrEmpty(referer)) return true;
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return false;
        var h = uri.Host;
        return h.Equals("cclabs.app", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".cclabs.app", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("ccp.", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The Referer the app's own fetch sends: none when the page sent none, the page's
    /// when it is ours, else ours.</summary>
    internal static string? OutgoingReferer(string? pageReferer)
        => string.IsNullOrEmpty(pageReferer) ? null : IsOurReferer(pageReferer) ? pageReferer : OurReferer;

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="sink"/>, tees every byte into the cache
    /// writer and charges as bytes flow (a cut download pays for what came). The writer is
    /// committed only on a whole body: end of stream, the expected length met, under the cap. A
    /// writer that fails is dropped; the page is still served. The writer is always disposed.
    /// True = whole. With <paramref name="idleTimeout"/>, a read that brings nothing for that long
    /// throws <see cref="OperationCanceledException"/> (a stalled body never hangs the page).
    /// </summary>
    internal static async Task<bool> TeeCopyAsync(Stream source, Stream sink, IScrolllerCacheWriter? writer,
        Action<long> charge, long? expectedLength, long maxBytes, CancellationToken ct, TimeSpan? idleTimeout = null)
    {
        var buf = new byte[81920];
        long total = 0, owed = 0;
        bool whole = false;
        using var idle = idleTimeout is TimeSpan ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
        var token = idle?.Token ?? ct;
        try
        {
            int n;
            while (true)
            {
                if (idle != null) idle.CancelAfter(idleTimeout!.Value);   // re-armed before every read
                n = await source.ReadAsync(buf.AsMemory(), token).ConfigureAwait(false);
                if (n <= 0) break;
                total += n;
                owed += n;
                if (owed >= ChargeBatchBytes) { charge(owed); owed = 0; }
                if (total > maxBytes) return false;
                sink.Write(buf, 0, n);
                if (writer != null)
                {
                    try { writer.Stream.Write(buf, 0, n); }
                    catch { try { writer.Dispose(); } catch { } writer = null; }
                }
            }
            whole = expectedLength is not long want || want == total;
            if (whole && writer != null)
            {
                try { writer.Stream.Flush(); writer.Commit(); }
                catch (Exception ex) { Log.Debug("ScrolllerWebGate: cache commit failed: {E}", ex.Message); }
            }
            return whole;
        }
        finally
        {
            if (owed > 0) charge(owed);
            // After a Commit this just closes the file; without one it throws the partial away.
            if (writer != null) { try { writer.Dispose(); } catch { } }
        }
    }

    /// <summary>Hooks the gate onto a CoreWebView2. Safe to call twice on the same core; never
    /// throws (a host that cannot be gated still runs, and logs why).</summary>
    public static void Attach(CoreWebView2? core)
    {
        if (core == null) return;
        try
        {
            if (Attached.TryGetValue(core, out _)) return;
            var state = new CoreState();
            Attached.Add(core, state);

            foreach (var pattern in new[] { "*://scrolller.com/*", "*://*.scrolller.com/*" })
            {
                try
                {
                    // Source kinds All also catches workers and iframes (runtime 1.0.2210+).
                    core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All,
                        CoreWebView2WebResourceRequestSourceKinds.All);
                }
                catch (Exception ex) when (ex is NotImplementedException or InvalidCastException
                                               or NotSupportedException)
                {
                    core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);
                }
            }

            core.WebResourceRequested += (_, e) => Handle(core, e);
            core.WebResourceResponseReceived += (_, e) => OnResponse(state, e);
            AttachRealBytes(core, state);
            // Clips pass to the network and land in Chromium's own disk cache: this core's profile
            // is cleared under the day limit (now when due, hourly, and on the off switch).
            TrackForCacheClears(core);
        }
        catch (Exception ex)
        {
            Log.Warning("ScrolllerWebGate: attach failed: {E}", ex.Message);
        }
    }

    private static void Handle(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            // TRAP: this handler also sees every other filter's requests on this core.
            var url = e.Request.Uri;
            if (Decide(url, ScrolllerPolicy.Enabled) == ScrolllerWebDecision.Pass) return;
            if (!ScrolllerPolicy.Enabled)
            {
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", LocalErrorHeaders());
                return;
            }

            var headers = e.Request.Headers;
            var pageReferer = headers.Contains("Referer") ? headers.GetHeader("Referer") : null;
            headers.SetHeader("User-Agent", ScrolllerSource.HonestUserAgent);
            if (pageReferer != null && !IsOurReferer(pageReferer))
                headers.SetHeader("Referer", OurReferer);

            var range = headers.Contains("Range") ? headers.GetHeader("Range") : null;
            var kind = KindOf(url, e.ResourceContext == CoreWebView2WebResourceContext.Media,
                e.ResourceContext == CoreWebView2WebResourceContext.Image);
            var path = kind == ScrolllerMediaKind.Other ? null : CachedPath(url);
            // A body already refused today (not media) is answered locally: no refetch, no charge.
            if (path == null && kind != ScrolllerMediaKind.Other && IsRefusedToday(url, DateTime.Now))
            {
                e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", LocalErrorHeaders());
                return;
            }
            var route = Route(url, e.Request.Method, true, path != null,
                ScrolllerPolicyService.MayFetchFresh(url), range != null, kind);

            switch (route)
            {
                case ScrolllerWebRoute.Refuse:
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", LocalErrorHeaders());
                    break;
                case ScrolllerWebRoute.PassCharged:
                    MarkPassed(url);
                    break;
                case ScrolllerWebRoute.ServeCached:
                    _ = ServeCachedAsync(core, e, e.GetDeferral(), url, path!, range);
                    break;
                case ScrolllerWebRoute.FetchWhole:
                    _ = ServeFetchedAsync(core, e, e.GetDeferral(), url, OutgoingReferer(pageReferer), kind);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("ScrolllerWebGate: request hook failed: {E}", ex.Message);
        }
    }

    /// <summary><see cref="ScrolllerDiskCache.TryGetPath"/> through a short memo (a hit for 30 s, a
    /// miss for 5 s). A memo that outlives its file is harmless: opening it fails and the request
    /// falls back as for an eviction.</summary>
    private static string? CachedPath(string url)
    {
        var now = Environment.TickCount64;
        if (PathMemo.TryGetValue(url, out var m) && now - m.Ticks < (m.Path != null ? PathMemoHitMs : PathMemoMissMs))
            return m.Path;
        var path = ScrolllerDiskCache.TryGetPath(url);
        if (PathMemo.Count > 1000) PathMemo.Clear();
        PathMemo[url] = (path, now);
        return path;
    }

    /// <summary>Answers from the cached file, off the UI thread, streamed from the file (no copy in
    /// memory). A file that vanished (evicted) goes out as a passed-through request if a fresh fetch
    /// is allowed, else a 403.</summary>
    private static async Task ServeCachedAsync(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2Deferral deferral, string url, string path, string? rangeHeader)
    {
        try
        {
            var read = await Task.Run(() => OpenCached(path, rangeHeader));
            var env = core.Environment;
            var type = ContentTypeFor(url);
            if (read == null)
            {
                PathMemo.TryRemove(url, out _);
                if (ScrolllerPolicyService.MayFetchFresh(url)) MarkPassed(url);
                else e.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", LocalErrorHeaders());
            }
            else if (read.Value.Unsatisfiable)
                e.Response = env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable",
                    LocalErrorHeaders($"Content-Range: bytes */{read.Value.Total}"));
            else if (read.Value.Range is ScrolllerByteRange r)
                e.Response = env.CreateWebResourceResponse(read.Value.Body, 206, "Partial Content",
                    ResponseHeaders(type, r.Length, r, read.Value.Total));
            else
                e.Response = env.CreateWebResourceResponse(read.Value.Body, 200, "OK",
                    ResponseHeaders(type, read.Value.Total));
        }
        catch (Exception ex) { Log.Debug("ScrolllerWebGate: cached answer failed: {E}", ex.Message); }
        finally { try { deferral.Complete(); } catch { } }
    }

    /// <summary>Opens the cached file for one answer: the asked range (206), else the WHOLE file
    /// (200; a request with no Range never gets a cut 206), or 416. Null when the file is gone.</summary>
    internal static (Stream? Body, ScrolllerByteRange? Range, long Total, bool Unsatisfiable)? OpenCached(string path, string? rangeHeader)
    {
        FileStream? fs = null;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                64 * 1024, FileOptions.SequentialScan);
            var total = fs.Length;
            if (TryParseRange(rangeHeader, total, out var r, out var bad))
            {
                if (bad) { fs.Dispose(); return (null, null, total, true); }
                return (new FileSliceStream(fs, r.Start, r.Length), r, total, false);
            }
            return (new FileSliceStream(fs, 0, total), null, total, false);
        }
        catch
        {
            fs?.Dispose();
            return null;
        }
    }

    /// <summary>Fetches a picture or GIF whole (one download per url however many tags ask),
    /// then answers the page. Too big for memory = passed through and charged.</summary>
    private static async Task ServeFetchedAsync(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2Deferral deferral, string url, string? referer, ScrolllerMediaKind kind)
    {
        try
        {
            var task = InFlight.GetOrAdd(url, u => FetchWholeAsync(u, referer, kind));
            Fetched? f;
            try { f = await task; }
            finally { InFlight.TryRemove(new KeyValuePair<string, Task<Fetched?>>(url, task)); }

            var env = core.Environment;
            if (f == null) MarkPassed(url);
            else if (f.Status == 200)
                e.Response = env.CreateWebResourceResponse(new MemoryStream(f.Body, 0, f.Length, writable: false), 200, "OK",
                    ResponseHeaders(f.ContentType, f.Length));
            else
                e.Response = env.CreateWebResourceResponse(null, f.Status, f.Reason, LocalErrorHeaders());
        }
        catch (Exception ex) { Log.Debug("ScrolllerWebGate: fetch answer failed: {E}", ex.Message); }
        finally { try { deferral.Complete(); } catch { } }
    }

    private static async Task<Fetched?> FetchWholeAsync(string url, string? referer, ScrolllerMediaKind kind)
    {
        // Bounded on every path: the headers and the body share a total budget, and the body read
        // gives up after FetchIdleTimeout without a byte. Every page waiting on it then gets a 502.
        using var budget = new CancellationTokenSource(FetchTotalTimeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (referer != null && Uri.TryCreate(referer, UriKind.Absolute, out var r)) req.Headers.Referrer = r;
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return new Fetched((int)res.StatusCode, res.ReasonPhrase ?? "Error", Array.Empty<byte>(), 0, "");
            var expected = res.Content.Headers.ContentLength;
            if (expected > MaxBodyBytes) return null;
            var type = res.Content.Headers.ContentType?.MediaType ?? ContentTypeFor(url);
            // Only a picture or GIF body is kept: an error page or anything else served with a
            // media extension is answered once and never replayed from the cache.
            IScrolllerCacheWriter? writer = null;
            var meta = ScrolllerItemIndex.MetaFor(url)
                       ?? new ScrolllerCacheMeta(kind == ScrolllerMediaKind.Gif ? "gif" : "picture");
            if (IsCacheableType(type)) writer = ScrolllerDiskCache.BeginWrite(url, meta);
            await using var body = await res.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
            using var sink = new MemoryStream(expected is long n && n > 0 ? (int)n : 0);
            // The share is per account: a body that spans a sign-in or sign-out charges each chunk to
            // whoever is signed in when it arrives (one body at most MaxBodyBytes).
            var whole = await TeeCopyAsync(body, sink, writer, ScrolllerAllowance.Charge, expected, MaxBodyBytes,
                budget.Token, FetchIdleTimeout).ConfigureAwait(false);
            // Paid for but never cached (not media): remembered for the day, so later page views
            // get a local 404 instead of fetching and paying for it again.
            if (whole && ScrolllerDiskCache.FormatCheck && RefusedBody(url, meta.Kind, sink.GetBuffer(), (int)sink.Length))
                RefuseForToday(url, DateTime.Now);
            // GetBuffer, not ToArray: the page is answered from this buffer, never a second copy.
            return whole
                ? new Fetched(200, "OK", sink.GetBuffer(), (int)sink.Length, type)
                : new Fetched(502, "Bad Gateway", Array.Empty<byte>(), 0, "");
        }
        catch (Exception ex)
        {
            Log.Debug("ScrolllerWebGate: fetch failed: {E}", ex.Message);
            return new Fetched(502, "Bad Gateway", Array.Empty<byte>(), 0, "");
        }
    }

    /// <summary>A whole-fetched body whose bytes the device cache's check refuses
    /// (<see cref="ScrolllerDiskCache.BodyFits"/>): an error page, not media. Decided by the bytes,
    /// never the content type: a CDN may serve a real picture as application/octet-stream. Pure.</summary>
    internal static bool RefusedBody(string url, string? kind, byte[] body, int length)
    {
        int n = Math.Min(length, ConditioningControlPanel.Helpers.MediaTypeSniffer.MagicProbeBytes);
        return n > 0 && !ScrolllerDiskCache.BodyFits(ScrolllerDiskCache.ExtensionOf(url), kind, body, n);
    }

    private static readonly HashSet<string> RefusedUrls = new(StringComparer.Ordinal);
    private static string? RefusedDay;
    private const int RefusedSoftCap = 5000;

    /// <summary>Remembers a refused url until local midnight. Machine-wide on purpose: the body is
    /// not media for anyone, so another account signed in the same day is spared the fetch too.</summary>
    internal static void RefuseForToday(string url, DateTime now)
    {
        var day = ScrolllerLedger.DayOf(now);
        lock (RefusedUrls)
        {
            if (RefusedDay != day || RefusedUrls.Count >= RefusedSoftCap) { RefusedUrls.Clear(); RefusedDay = day; }
            RefusedUrls.Add(url);
        }
    }

    internal static bool IsRefusedToday(string url, DateTime now)
    {
        var day = ScrolllerLedger.DayOf(now);
        lock (RefusedUrls) return RefusedDay == day && RefusedUrls.Contains(url);
    }

    /// <summary>Tests only.</summary>
    internal static void ClearRefusedForTests()
    {
        lock (RefusedUrls) { RefusedUrls.Clear(); RefusedDay = null; }
    }

    /// <summary>Only image bodies go into the device cache from a whole fetch.</summary>
    internal static bool IsCacheableType(string? mediaType)
        => mediaType != null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    // ---- passed-on requests --------------------------------------------------------------

    /// <summary>Notes one request the gate passed on to the network (it owes a charge).</summary>
    internal static void MarkPassed(string url)
    {
        var now = Environment.TickCount64;
        Passed.AddOrUpdate(url, (1, now), (_, v) => (v.Count + 1, now));
        if (Passed.Count > PassedSoftCap)
        {
            // Only marks older than the TTL go: a response still on its way keeps its charge.
            foreach (var kv in Passed)
                if (now - kv.Value.Ticks > PassedTtlMs) Passed.TryRemove(kv);
        }
    }

    /// <summary>Takes one passed-on mark for <paramref name="url"/>; false when the gate never
    /// passed it on (its own answer, another filter's request, or already charged).</summary>
    internal static bool TakePassed(string url)
    {
        while (Passed.TryGetValue(url, out var v))
        {
            if (v.Count <= 1)
            {
                if (Passed.TryRemove(new KeyValuePair<string, (int, long)>(url, v))) return true;
            }
            else if (Passed.TryUpdate(url, (v.Count - 1, v.Ticks), v)) return true;
        }
        return false;
    }

    // ---- charging ------------------------------------------------------------------------

    /// <summary>Fallback charge for a core without the DevTools meter: a passed-through media
    /// response by its declared length. Only requests the gate passed on itself count (never its
    /// own answers, never another filter's requests).</summary>
    private static void OnResponse(CoreState state, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            if (state.RealBytes) return;   // the DevTools meter charges this core
            var url = e.Request.Uri;
            if (url == null || !TakePassed(url)) return;

            var h = e.Response.Headers;
            if (h.Contains(MarkerHeader)) return;
            var status = e.Response.StatusCode;
            if (status != 200 && status != 206) return;
            var length = h.Contains("Content-Length") ? h.GetHeader("Content-Length") : null;
            var range = h.Contains("Content-Range") ? h.GetHeader("Content-Range") : null;
            var bytes = ChargeableLength(length, range);
            if (bytes <= 0)
            {
                Log.Debug("ScrolllerWebGate: no length on a passed-through response, not charged");
                return;
            }
            ChargeCapped(url, bytes, TotalSize(status, length, range));
        }
        catch (Exception ex) { Log.Debug("ScrolllerWebGate: response hook failed: {E}", ex.Message); }
    }

    /// <summary>Charges <paramref name="bytes"/> for a passed-through url, never past the file's
    /// whole size in one local day (Chromium's own cache can replay a url, and ranged requests
    /// overlap).</summary>
    private static void ChargeCapped(string url, long bytes, long? total)
    {
        var day = ScrolllerLedger.DayOf(DateTime.Now);
        // Per account, like the share: another account replaying the same url pays its own day.
        var key = ScrolllerAllowance.AccountKey + "|" + url;
        long due;
        lock (ChargedToday)
        {
            if (ChargedDay != day) { ChargedToday.Clear(); ChargedDay = day; }
            ChargedToday.TryGetValue(key, out var already);
            due = DedupedCharge(already, bytes, total);
            if (due > 0) ChargedToday[key] = already + due;
        }
        if (due > 0) ScrolllerAllowance.Charge(due);
    }

    private static readonly Dictionary<string, long> ChargedToday = new(StringComparer.Ordinal);
    private static string? ChargedDay;

    /// <summary>What a passed-through response still owes today: never past the file's whole
    /// <paramref name="total"/> for the day; with no known total, only the first response pays.</summary>
    internal static long DedupedCharge(long already, long bytes, long? total)
    {
        if (bytes <= 0) return 0;
        if (total is long t && t > 0) return Math.Max(0, Math.Min(bytes, t - already));
        return already > 0 ? 0 : bytes;
    }

    /// <summary>The whole file's size: a 206's "bytes a-b/TOTAL", a 200's Content-Length, else null.</summary>
    internal static long? TotalSize(int status, string? contentLength, string? contentRange)
    {
        if (status == 206)
        {
            var slash = contentRange?.LastIndexOf('/') ?? -1;
            return slash >= 0 && long.TryParse(contentRange!.AsSpan(slash + 1), out var t) && t > 0 ? t : null;
        }
        return long.TryParse(contentLength, out var n) && n > 0 ? n : null;
    }

    // ---- real bytes (DevTools Network domain) --------------------------------------------

    /// <summary>
    /// Turns on the DevTools Network domain for this core (no body buffering) and feeds the
    /// meter. Events arrive on the UI thread; dataReceived returns at once while no Scrolller
    /// response is live, so a page's own traffic costs one event hop and nothing more. No other
    /// code in the app speaks DevTools on these cores (the Back Room's DEBUG remote port is a
    /// separate session). If Network.enable fails, the core keeps the declared-length charge.
    /// </summary>
    private static void AttachRealBytes(CoreWebView2 core, CoreState state)
    {
        try
        {
            var meter = state.Meter;
            Subscribe(core, "Network.responseReceived", json =>
            {
                var r = ParseCdpResponse(json);
                if (r == null || !ShouldMeter(r) || !TakePassed(r.Url)) return;
                meter.Start(r.RequestId, r.Url, r.Total);
            });
            Subscribe(core, "Network.dataReceived", json =>
            {
                if (meter.LiveCount == 0) return;
                if (TryReadIdAndLength(json, "encodedDataLength", out var id, out var n)) meter.Data(id, n);
            });
            Subscribe(core, "Network.loadingFinished", json =>
            {
                if (meter.LiveCount == 0) return;
                if (TryReadIdAndLength(json, "encodedDataLength", out var id, out var n)) meter.End(id, n);
            });
            Subscribe(core, "Network.loadingFailed", json =>
            {
                if (meter.LiveCount == 0) return;
                if (TryReadIdAndLength(json, null, out var id, out _)) meter.End(id, null);
            });
            _ = EnableNetworkAsync(core, state);
        }
        catch (Exception ex)
        {
            Log.Debug("ScrolllerWebGate: real-byte meter unavailable, declared lengths charge: {E}", ex.Message);
        }
    }

    private static void Subscribe(CoreWebView2 core, string evt, Action<string> handler)
    {
        core.GetDevToolsProtocolEventReceiver(evt).DevToolsProtocolEventReceived += (_, e) =>
        {
            try { handler(e.ParameterObjectAsJson); }
            catch (Exception ex) { Log.Debug("ScrolllerWebGate: {Evt} failed: {E}", evt, ex.Message); }
        };
    }

    private static async Task EnableNetworkAsync(CoreWebView2 core, CoreState state)
    {
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Network.enable", NetworkEnableParams);
            state.RealBytes = true;
        }
        catch (Exception ex)
        {
            Log.Debug("ScrolllerWebGate: Network.enable failed, declared lengths charge: {E}", ex.Message);
        }
    }

    /// <summary>One Network.responseReceived, reduced to what the meter needs.</summary>
    internal sealed record CdpResponse(string RequestId, string Url, int Status, bool OurAnswer, long? Total);

    /// <summary>Parses a Network.responseReceived payload; null when it is not one.</summary>
    internal static CdpResponse? ParseCdpResponse(string? json)
    {
        // Cheap reject first: every response on the page comes through here.
        if (string.IsNullOrEmpty(json) || json.IndexOf("scrolller.com", StringComparison.OrdinalIgnoreCase) < 0) return null;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("requestId", out var idEl) || idEl.ValueKind != JsonValueKind.String) return null;
        if (!root.TryGetProperty("response", out var res) || res.ValueKind != JsonValueKind.Object) return null;
        var url = res.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
        if (string.IsNullOrEmpty(url)) return null;
        int status = res.TryGetProperty("status", out var s) && s.TryGetInt32(out var si) ? si : 0;
        string? length = null, range = null;
        bool ours = false;
        if (res.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in hs.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) continue;
                if (p.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) length = p.Value.GetString();
                else if (p.Name.Equals("Content-Range", StringComparison.OrdinalIgnoreCase)) range = p.Value.GetString();
                else if (p.Name.Equals(MarkerHeader, StringComparison.OrdinalIgnoreCase)) ours = true;
            }
        }
        return new CdpResponse(idEl.GetString()!, url!, status, ours, TotalSize(status, length, range));
    }

    /// <summary>A response the meter charges: Scrolller, not the API host, not one of the gate's own
    /// answers (their bodies DO report encodedDataLength). Chromium's cache needs no rule here:
    /// a replay reports 0 encoded bytes, and a range served partly from the cache (fromDiskCache
    /// is then true) reports only the bytes that came over the network (probed on runtime 155).</summary>
    internal static bool ShouldMeter(CdpResponse r)
    {
        if (r.OurAnswer || (r.Status != 200 && r.Status != 206)) return false;
        if (!Uri.TryCreate(r.Url, UriKind.Absolute, out var uri)) return false;
        return IsScrolllerHost(uri.Host) && !IsApiHost(uri.Host);
    }

    /// <summary>Reads requestId and one numeric field (if named) from a Network event payload.</summary>
    internal static bool TryReadIdAndLength(string? json, string? lengthField, out string requestId, out long length)
    {
        requestId = "";
        length = 0;
        if (string.IsNullOrEmpty(json)) return false;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("requestId", out var idEl) || idEl.ValueKind != JsonValueKind.String) return false;
        requestId = idEl.GetString()!;
        if (lengthField != null && root.TryGetProperty(lengthField, out var l) && l.ValueKind == JsonValueKind.Number)
            length = (long)l.GetDouble();
        return true;
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ScrolllerSource.HonestUserAgent);
        return c;
    }

    private static string ExtensionOf(string? requestUri)
    {
        if (string.IsNullOrEmpty(requestUri) || !Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)) return "";
        try { return Path.GetExtension(uri.AbsolutePath).ToLowerInvariant(); }
        catch { return ""; }
    }
}

/// <summary>
/// Real bytes for passed-through Scrolller responses, from the DevTools Network domain:
/// Network.dataReceived's encodedDataLength is what came over the wire, so a clip the player cuts
/// short pays only for what arrived. Charged every <see cref="ScrolllerWebGate.ChargeBatchBytes"/>
/// and at the end (loadingFinished: the larger of the running sum and its own total; loadingFailed:
/// the running sum). One meter per core (request ids are per browser); UI thread only.
/// </summary>
internal sealed class ScrolllerRealBytesMeter
{
    /// <summary>Requests left open by a page that went away are settled and dropped past this.</summary>
    internal const int MaxLive = 256;

    private sealed class Live
    {
        public string Url = "";
        public long? Total;
        public long Seen, Charged;
    }

    private readonly Dictionary<string, Live> _live = new(StringComparer.Ordinal);
    private readonly Action<string, long, long?> _charge;

    /// <param name="charge">url, bytes, the file's whole size if known.</param>
    public ScrolllerRealBytesMeter(Action<string, long, long?> charge) => _charge = charge;

    public int LiveCount => _live.Count;

    public void Start(string requestId, string url, long? total)
    {
        if (_live.Count >= MaxLive)
        {
            foreach (var l in _live.Values) Settle(l, l.Seen);
            _live.Clear();
        }
        if (_live.Remove(requestId, out var old)) Settle(old, old.Seen);   // a redirect reuses the id
        _live[requestId] = new Live { Url = url, Total = total };
    }

    public void Data(string requestId, long encodedBytes)
    {
        if (encodedBytes <= 0 || !_live.TryGetValue(requestId, out var l)) return;
        l.Seen += encodedBytes;
        if (l.Seen - l.Charged >= ScrolllerWebGate.ChargeBatchBytes) Settle(l, l.Seen);
    }

    /// <param name="finishedEncodedLength">loadingFinished's total, or null for loadingFailed.</param>
    public void End(string requestId, long? finishedEncodedLength)
    {
        if (!_live.Remove(requestId, out var l)) return;
        Settle(l, Math.Max(l.Seen, finishedEncodedLength ?? 0));
    }

    private void Settle(Live l, long upTo)
    {
        var owed = upTo - l.Charged;
        if (owed <= 0) return;
        l.Charged = upTo;
        _charge(l.Url, owed, l.Total);
    }
}

/// <summary>A read-only window [start, start+length) of a file, closing the file at its end. The
/// gate hands it to WebView2 so a cached answer streams from disk instead of a byte[] copy.</summary>
internal sealed class FileSliceStream : Stream
{
    private readonly FileStream _fs;
    private readonly long _start, _length;
    private long _pos;
    private bool _closed;

    public FileSliceStream(FileStream fs, long start, long length)
    {
        _fs = fs;
        _start = start;
        _length = Math.Max(0, Math.Min(length, fs.Length - start));
        _fs.Position = start;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _pos;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>Reads within the window; the file closes as soon as the window is read to its
    /// end (WebView2 reads a body once, front to back), so an evicted file is never held open.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_closed) return 0;
        var want = (int)Math.Min(count, _length - _pos);
        if (want <= 0) { Close(); return 0; }
        var n = _fs.Read(buffer, offset, want);
        _pos += n;
        if (n == 0 || _pos >= _length) Close();
        return n;
    }

    /// <summary>Moves within the window. After the file closed this only moves the position
    /// (a caller asking where it is, or for the size, still gets an answer).</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _pos + offset,
            _ => _length + offset,
        };
        _pos = Math.Max(0, Math.Min(_length, target));
        if (!_closed) _fs.Position = _start + _pos;
        return _pos;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_closed)
        {
            _closed = true;
            if (disposing) _fs.Dispose();
        }
        base.Dispose(disposing);
    }
}
