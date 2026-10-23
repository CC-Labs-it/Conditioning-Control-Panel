using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Fyp.Online;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>What the gate does with one request a page makes (<see cref="ScrolllerWebView2Gate.Route"/>).</summary>
internal enum ScrolllerGateRoute
{
    /// <summary>Not Scrolller: left alone.</summary>
    Pass,
    /// <summary>The licence switch is off: a local 403, nothing leaves the machine.</summary>
    Block,
    /// <summary>The API, the site, anything not media or not GET: goes out with the honest user-agent, not charged.</summary>
    Rewrite,
    /// <summary>Media with a cached copy: answered from the file, zero network, zero charge.</summary>
    ServeCached,
    /// <summary>Media, not cached, a fresh fetch allowed: the device cache downloads it (honest user-agent, charged as bytes
    /// arrive, born at download for the 24 h sweep), then it is answered from the file.</summary>
    FetchThenServe,
    /// <summary>Media, not cached, no fresh fetch allowed (share spent, away, not settled): a local 403, the page falls back.</summary>
    Refuse,
}

/// <summary>
/// The Scrolller gate on this head's Windows web views (WPF Services/Fyp/Online/ScrolllerWebGate, licence 2026-10-22):
/// hooked on the CoreWebView2 behind every <see cref="Views.Controls.WebHost"/>, so a page that loads Scrolller itself
/// (the in-app browser, Just Drop, the Deeper preview, the leash punishment page) never goes out with the engine's
/// user-agent, uncharged and uncached. The same rule as this head's loopback relay (Core <see cref="ScrolllerRelay"/>):
/// media is ALWAYS answered from the device cache (a fresh copy fetched whole first), so no uncached url is ever streamed,
/// every byte is charged as it arrives, and every answer is no-store (Chromium keeps no second copy). WPF lets clips pass
/// to the network and meters them over DevTools; this head never needs that, as the relay already showed.
/// Avalonia.Controls.WebView exposes no response seam, so the core comes from its platform handle
/// (<c>IWindowsWebView2PlatformHandle.CoreWebView2</c>) through the WebView2 SDK's managed wrapper. Never throws.
/// </summary>
internal static class ScrolllerWebView2Gate
{
    internal const string OurReferer = "https://cclabs.app/";

    /// <summary>An open-ended Range on a cached file gets at most this much (WPF SliceBytes: WebView2 holds a response
    /// until its stream ends, so a clip plays on capped slices), an explicit one at most <see cref="MaxBodyBytes"/>.</summary>
    internal const long SliceBytes = 2L * 1024 * 1024, MaxBodyBytes = 64L * 1024 * 1024;

    private static readonly TimeSpan FetchBudget = TimeSpan.FromMinutes(3);
    private static readonly ConditionalWeakTable<CoreWebView2, object> Attached = new();

    /// <summary>Test seams: the cached copy, the share rule, the device cache's read-or-fetch.</summary>
    internal static Func<string, string?> Cached = ScrolllerDiskCache.TryGetPath;
    internal static Func<string, bool> MayFetchFresh = ScrolllerPolicyService.MayFetchFresh;
    internal static Func<string, CancellationToken, Task<string?>> Fetch =
        (url, ct) => ScrolllerDiskCache.GetOrFetchAsync(url, ScrolllerItemIndex.MetaFor(url), ct);

    /// <summary>The whole rule, pure. Not Scrolller = pass; switch off = block (the API too); not GET or not media =
    /// honest UA only; then the relay's own rule: cached, else a fresh fetch only when allowed.</summary>
    internal static ScrolllerGateRoute Route(string? url, string? method, bool enabled, bool cached, bool mayFetchFresh,
        bool mediaContext = false)
    {
        if (!ScrolllerPolicy.IsScrolllerUrl(url)) return ScrolllerGateRoute.Pass;
        if (!enabled) return ScrolllerGateRoute.Block;
        if (!string.Equals(method ?? "GET", "GET", StringComparison.OrdinalIgnoreCase) || !IsMedia(url!, mediaContext))
            return ScrolllerGateRoute.Rewrite;
        return ScrolllerRelay.Decide(url, true, cached, mayFetchFresh) switch
        {
            ScrolllerRelayRoute.ServeCached => ScrolllerGateRoute.ServeCached,
            ScrolllerRelayRoute.FetchThenServe => ScrolllerGateRoute.FetchThenServe,
            _ => ScrolllerGateRoute.Refuse,
        };
    }

    /// <summary>Media: a media extension on a CDN host (the relay's rule), or an extensionless url the page loads as an
    /// image or a video (WPF KindOf's resource-context fallback), so no media url slips out uncached.</summary>
    internal static bool IsMedia(string url, bool mediaContext)
    {
        if (ScrolllerRelay.ToRelayPath(url) != null) return true;
        if (!mediaContext || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.Host.TrimEnd('.').ToLowerInvariant();
        return host.EndsWith(".scrolller.com", StringComparison.Ordinal) && ScrolllerRelay.IsMediaSub(host[..^".scrolller.com".Length]);
    }

    /// <summary>A Referer that is ours rides along; any other (a remote site, this head's tokened loopback url) becomes ours.</summary>
    internal static string? OutgoingReferer(string? pageReferer)
    {
        if (string.IsNullOrEmpty(pageReferer)) return null;
        if (Uri.TryCreate(pageReferer, UriKind.Absolute, out var u)
            && (u.Host.Equals("cclabs.app", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".cclabs.app", StringComparison.OrdinalIgnoreCase)))
            return pageReferer;
        return OurReferer;
    }

    /// <summary>The slice one answer carries: (from, to) inclusive, (-1,-1) = 416, null = the whole file (200).</summary>
    internal static (long From, long To)? SliceFor(string? rangeHeader, long length)
    {
        if (WebAssetServer.ParseRange(rangeHeader, length) is not { } r) return null;
        if (r.From < 0) return r;
        bool openEnded = rangeHeader!.TrimEnd().EndsWith('-');
        long cap = openEnded ? SliceBytes : MaxBodyBytes;
        return (r.From, Math.Min(r.To, r.From + cap - 1));
    }

    /// <summary>Hooks one core (a raw ICoreWebView2 from the adapter's platform handle). Safe twice; never throws.</summary>
    public static void Attach(IntPtr coreWebView2)
    {
        if (coreWebView2 == IntPtr.Zero) return;
        try
        {
            var core = CoreWebView2.CreateFromComICoreWebView2(coreWebView2);
            if (Attached.TryGetValue(core, out _)) return;
            Attached.Add(core, new object());
            foreach (var pattern in new[] { "*://scrolller.com/*", "*://*.scrolller.com/*" })
            {
                try
                {
                    core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                }
                catch (Exception ex) when (ex is NotImplementedException or InvalidCastException or NotSupportedException)
                {
                    core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);
                }
            }
            core.WebResourceRequested += (_, e) => Handle(core, e);
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebView2Gate: attach failed: {E}", ex.Message); }
    }

    private static void Handle(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            // TRAP (WPF): this handler also sees every other filter's requests on this core.
            var url = e.Request.Uri;
            if (!ScrolllerPolicy.IsScrolllerUrl(url)) return;
            var enabled = ScrolllerPolicy.Enabled;
            var cached = enabled ? Cached(url) : null;
            var mediaContext = e.ResourceContext is CoreWebView2WebResourceContext.Image or CoreWebView2WebResourceContext.Media;
            var route = Route(url, e.Request.Method, enabled, cached != null, enabled && cached == null && MayFetchFresh(url), mediaContext);
            var env = core.Environment;
            switch (route)
            {
                case ScrolllerGateRoute.Block:
                case ScrolllerGateRoute.Refuse:
                    e.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", ErrorHeaders());
                    return;
                case ScrolllerGateRoute.Rewrite:
                    var headers = e.Request.Headers;
                    headers.SetHeader("User-Agent", ScrolllerSource.HonestUserAgent);
                    if (headers.Contains("Referer") && OutgoingReferer(headers.GetHeader("Referer")) is { } referer)
                        headers.SetHeader("Referer", referer);
                    return;
                case ScrolllerGateRoute.ServeCached:
                case ScrolllerGateRoute.FetchThenServe:
                    var range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
                    _ = AnswerAsync(env, e, e.GetDeferral(), url, cached, range);
                    return;
            }
        }
        catch (Exception ex) { Log.Warning("ScrolllerWebView2Gate: request hook failed: {E}", ex.Message); }
    }

    /// <summary>Answers from the device cache's file, fetching it first when it is not there. The file work runs off the
    /// UI thread; the response is set and the deferral completed back on it (WebView2 objects are thread-bound).</summary>
    private static async Task AnswerAsync(CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2Deferral deferral, string url, string? cached, string? range)
    {
        (Stream? Body, int Status, string Reason, string Headers) answer;
        try
        {
            answer = await Task.Run(async () =>
            {
                var path = cached;
                if (path == null)
                {
                    using var budget = new CancellationTokenSource(FetchBudget);
                    try { path = await Fetch(url, budget.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { path = null; }
                }
                return Open(path, url, range);
            });
        }
        catch (Exception ex)
        {
            Log.Debug("ScrolllerWebView2Gate: answer failed: {E}", ex.Message);
            answer = (null, 502, "Bad Gateway", ErrorHeaders());
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try { e.Response = env.CreateWebResourceResponse(answer.Body, answer.Status, answer.Reason, answer.Headers); }
            catch (Exception ex) { Log.Debug("ScrolllerWebView2Gate: response failed: {E}", ex.Message); }
            finally { try { deferral.Complete(); } catch { } }
        });
    }

    /// <summary>One answer from a file: the slice asked for (206), else the whole file (200), 416 past the end, 502 when
    /// there is no file. Always no-store, any origin may read it (pages load Scrolller cross-origin, as from the CDN).</summary>
    internal static (Stream? Body, int Status, string Reason, string Headers) Open(string? path, string url, string? range)
    {
        FileStream? fs = null;
        try
        {
            if (path == null || !File.Exists(path)) return (null, 502, "Bad Gateway", ErrorHeaders());
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            var total = fs.Length;
            var type = ScrolllerRelay.ContentTypeFor(url);
            switch (SliceFor(range, total))
            {
                case null:
                    return (fs, 200, "OK", Headers(type, total, null, total));
                case { From: < 0 }:
                    fs.Dispose();
                    return (null, 416, "Range Not Satisfiable", ErrorHeaders($"Content-Range: bytes */{total}"));
                case var (from, to):
                    var len = to - from + 1;
                    var body = new MemoryStream(new byte[len], 0, (int)len, writable: true, publiclyVisible: true);
                    fs.Position = from;
                    fs.ReadExactly(body.GetBuffer(), 0, (int)len);
                    fs.Dispose();
                    return (body, 206, "Partial Content", Headers(type, len, (from, to), total));
            }
        }
        catch (Exception ex)
        {
            fs?.Dispose();
            Log.Debug("ScrolllerWebView2Gate: cached file unreadable: {E}", ex.Message);
            return (null, 502, "Bad Gateway", ErrorHeaders());
        }
    }

    internal static string Headers(string type, long length, (long From, long To)? range, long total)
    {
        var s = $"Content-Type: {type}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\n";
        if (range is var (from, to)) s += $"Content-Range: bytes {from}-{to}/{total}\r\n";
        return s + "Cache-Control: no-store\r\nAccess-Control-Allow-Origin: *";
    }

    /// <summary>The gate's own refusals: any origin may read them, so a page's fetch sees the status, not a CORS error.</summary>
    internal static string ErrorHeaders(string? extra = null)
        => (string.IsNullOrEmpty(extra) ? "" : extra + "\r\n") + "Cache-Control: no-store\r\nAccess-Control-Allow-Origin: *";
}
