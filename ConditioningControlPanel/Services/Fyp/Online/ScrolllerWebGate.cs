using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

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
/// charged as bytes arrive), clips and ranged requests go out as before and are charged by the
/// response length in <c>WebResourceResponseReceived</c>, and a cached clip is answered in capped
/// 206 slices (a video loads, seeks and plays to the end on them, same probe).
/// </summary>
internal static class ScrolllerWebGate
{
    internal const string OurReferer = "https://cclabs.app/";

    /// <summary>Marks a response the gate made itself, so the response hook never charges it.</summary>
    internal const string MarkerHeader = "X-CCP-Gate";

    /// <summary>An open-ended Range (<c>bytes=N-</c>) on a cached file gets at most this much.</summary>
    internal const long SliceBytes = 2L * 1024 * 1024;

    /// <summary>Largest body the gate holds in memory (an explicit range, or a whole fetch).</summary>
    internal const long MaxBodyBytes = 64L * 1024 * 1024;

    private const int ChargeBatchBytes = 256 * 1024;

    private static readonly ConditionalWeakTable<CoreWebView2, object> Attached = new();
    private static readonly ConcurrentDictionary<string, int> PendingCharge = new();
    private static readonly ConcurrentDictionary<string, Task<Fetched?>> InFlight = new();
    private static readonly HttpClient Http = CreateHttp();

    private sealed record Fetched(int Status, string Reason, byte[] Body, string ContentType);

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
    /// (ccp.game, fetch and WebGL textures), so it allows any origin as the CDN does.</summary>
    internal static string ResponseHeaders(string contentType, long length, ScrolllerByteRange? range = null, long total = 0)
    {
        var s = $"Content-Type: {contentType}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\n";
        if (range is ScrolllerByteRange r) s += $"Content-Range: bytes {r.Start}-{r.End}/{total}\r\n";
        return s + $"Access-Control-Allow-Origin: *\r\n{MarkerHeader}: local";
    }

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
    /// True = whole.
    /// </summary>
    internal static async Task<bool> TeeCopyAsync(Stream source, Stream sink, IScrolllerCacheWriter? writer,
        Action<long> charge, long? expectedLength, long maxBytes, CancellationToken ct)
    {
        var buf = new byte[81920];
        long total = 0, owed = 0;
        bool whole = false;
        try
        {
            int n;
            while ((n = await source.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
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
                catch (Exception ex) { App.Logger?.Debug("ScrolllerWebGate: cache commit failed: {E}", ex.Message); }
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
            Attached.Add(core, new object());

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
            core.WebResourceResponseReceived += (_, e) => OnResponse(e);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ScrolllerWebGate: attach failed: {E}", ex.Message);
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
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
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
            var path = kind == ScrolllerMediaKind.Other ? null : ScrolllerDiskCache.TryGetPath(url);
            var route = Route(url, e.Request.Method, true, path != null,
                ScrolllerPolicyService.MayFetchFresh(url), range != null, kind);

            switch (route)
            {
                case ScrolllerWebRoute.Refuse:
                    e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", $"{MarkerHeader}: local");
                    break;
                case ScrolllerWebRoute.PassCharged:
                    PendingCharge.AddOrUpdate(url, 1, (_, n) => n + 1);
                    if (PendingCharge.Count > 2000) PendingCharge.Clear();
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
            App.Logger?.Warning("ScrolllerWebGate: request hook failed: {E}", ex.Message);
        }
    }

    /// <summary>Answers from the cached file, off the UI thread. A file that vanished (evicted)
    /// goes out as a passed-through request if a fresh fetch is allowed, else a 403.</summary>
    private static async Task ServeCachedAsync(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2Deferral deferral, string url, string path, string? rangeHeader)
    {
        try
        {
            var read = await Task.Run(() => ReadCached(path, rangeHeader));
            var env = core.Environment;
            var type = ContentTypeFor(url);
            if (read == null)
            {
                if (ScrolllerPolicyService.MayFetchFresh(url)) PendingCharge.AddOrUpdate(url, 1, (_, n) => n + 1);
                else e.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", $"{MarkerHeader}: local");
            }
            else if (read.Value.Unsatisfiable)
                e.Response = env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable",
                    $"Content-Range: bytes */{read.Value.Total}\r\n{MarkerHeader}: local");
            else if (read.Value.Range is ScrolllerByteRange r)
                e.Response = env.CreateWebResourceResponse(new MemoryStream(read.Value.Body), 206, "Partial Content",
                    ResponseHeaders(type, read.Value.Body.Length, r, read.Value.Total));
            else
                e.Response = env.CreateWebResourceResponse(new MemoryStream(read.Value.Body), 200, "OK",
                    ResponseHeaders(type, read.Value.Body.Length));
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerWebGate: cached answer failed: {E}", ex.Message); }
        finally { try { deferral.Complete(); } catch { } }
    }

    private static (byte[] Body, ScrolllerByteRange? Range, long Total, bool Unsatisfiable)? ReadCached(string path, string? rangeHeader)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var total = fs.Length;
            ScrolllerByteRange? range = null;
            if (TryParseRange(rangeHeader, total, out var r, out var bad))
            {
                if (bad) return (Array.Empty<byte>(), null, total, true);
                range = r;
            }
            else if (total > MaxBodyBytes) range = new ScrolllerByteRange(0, SliceBytes - 1);
            long start = range?.Start ?? 0, len = range?.Length ?? total;
            var body = new byte[len];
            fs.Position = start;
            fs.ReadExactly(body);
            return (body, range, total, false);
        }
        catch { return null; }
    }

    /// <summary>Fetches a picture or GIF whole (one download per url however many tags ask),
    /// then answers the page. Too big for memory = passed through and charged by length.</summary>
    private static async Task ServeFetchedAsync(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2Deferral deferral, string url, string? referer, ScrolllerMediaKind kind)
    {
        try
        {
            var task = InFlight.GetOrAdd(url, u => FetchWholeAsync(u, referer, kind));
            Fetched? f;
            try { f = await task; }
            finally { InFlight.TryRemove(new System.Collections.Generic.KeyValuePair<string, Task<Fetched?>>(url, task)); }

            var env = core.Environment;
            if (f == null) PendingCharge.AddOrUpdate(url, 1, (_, n) => n + 1);
            else if (f.Status == 200)
                e.Response = env.CreateWebResourceResponse(new MemoryStream(f.Body), 200, "OK",
                    ResponseHeaders(f.ContentType, f.Body.Length));
            else
                e.Response = env.CreateWebResourceResponse(null, f.Status, f.Reason, $"{MarkerHeader}: local");
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerWebGate: fetch answer failed: {E}", ex.Message); }
        finally { try { deferral.Complete(); } catch { } }
    }

    private static async Task<Fetched?> FetchWholeAsync(string url, string? referer, ScrolllerMediaKind kind)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (referer != null && Uri.TryCreate(referer, UriKind.Absolute, out var r)) req.Headers.Referrer = r;
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return new Fetched((int)res.StatusCode, res.ReasonPhrase ?? "Error", Array.Empty<byte>(), "");
            var expected = res.Content.Headers.ContentLength;
            if (expected > MaxBodyBytes) return null;
            var type = res.Content.Headers.ContentType?.MediaType ?? ContentTypeFor(url);
            var meta = ScrolllerItemIndex.MetaFor(url)
                       ?? new ScrolllerCacheMeta(kind == ScrolllerMediaKind.Gif ? "gif" : "picture");
            var writer = ScrolllerDiskCache.BeginWrite(url, meta);
            await using var body = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var sink = new MemoryStream(expected is long n && n > 0 ? (int)n : 0);
            var whole = await TeeCopyAsync(body, sink, writer, ScrolllerAllowance.Charge, expected, MaxBodyBytes,
                CancellationToken.None).ConfigureAwait(false);
            return whole
                ? new Fetched(200, "OK", sink.ToArray(), type)
                : new Fetched(502, "Bad Gateway", Array.Empty<byte>(), "");
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ScrolllerWebGate: fetch failed: {E}", ex.Message);
            return new Fetched(502, "Bad Gateway", Array.Empty<byte>(), "");
        }
    }

    /// <summary>Charges a passed-through media response by its length. Only requests the gate
    /// passed on itself count (never its own answers, never another filter's requests).</summary>
    private static void OnResponse(CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var url = e.Request.Uri;
            if (url == null || !PendingCharge.TryGetValue(url, out var n)) return;
            if (n <= 1) PendingCharge.TryRemove(url, out _);
            else PendingCharge[url] = n - 1;

            var h = e.Response.Headers;
            if (h.Contains(MarkerHeader)) return;
            var status = e.Response.StatusCode;
            if (status != 200 && status != 206) return;
            var bytes = ChargeableLength(h.Contains("Content-Length") ? h.GetHeader("Content-Length") : null,
                h.Contains("Content-Range") ? h.GetHeader("Content-Range") : null);
            if (bytes > 0) ScrolllerAllowance.Charge(bytes);
            else App.Logger?.Debug("ScrolllerWebGate: no length on a passed-through response, not charged");
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerWebGate: response hook failed: {E}", ex.Message); }
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
