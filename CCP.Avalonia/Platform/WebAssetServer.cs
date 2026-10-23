using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Quiz;
using ConditioningControlPanel.Services;
using Serilog;

using ConditioningControlPanel;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Serves Resources/web to this head's web views, standing in for WPF's WebView2
/// SetVirtualHostNameToFolderMapping("ccp.game", Resources\web). Avalonia.Controls.WebView 12.1 cannot
/// answer a custom scheme on WebKitGTK and file:// breaks module/fetch loads, so: plain HTTP on 127.0.0.1
/// only, a random port, and a per-run token. The first URL carries ?ccp_t=token; the answer sets an
/// HttpOnly SameSite=Strict cookie so the page's own relative and root-relative loads keep working.
/// Anything without the token, with a foreign Host header (DNS rebinding), or resolving outside the
/// root is refused.
/// </summary>
public sealed class WebAssetServer : IDisposable
{
    static readonly object _sharedLock = new();
    static WebAssetServer? _shared;

    /// <summary>The app-wide server over Resources/web, started on first use; a failed start is retried next time.</summary>
    public static WebAssetServer Shared
    {
        get
        {
            lock (_sharedLock)
                return _shared ??= new WebAssetServer(Path.Combine(AppContext.BaseDirectory, "Resources", "web"))
                    { AssetsRoot = () => CorePaths.EffectiveAssets, ContentRoot = () => ContentLocator.ContentWebRoot };
        }
    }

    readonly SemaphoreSlim _inFlight = new(16);

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".htm"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8", [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8", [".json"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8", [".svg"] = "image/svg+xml", [".png"] = "image/png",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".ico"] = "image/x-icon", [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".woff"] = "font/woff",
        [".woff2"] = "font/woff2", [".ttf"] = "font/ttf", [".wasm"] = "application/wasm",
    };

    readonly HttpListener _listener = new();
    readonly string _root;

    public int Port { get; }
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public WebAssetServer(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        // ponytail: probe-then-bind can lose the port to another process in between; retry if that ever shows up.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(Loop);
    }

    /// <summary>The URL a web view navigates to for a page under the root, e.g. "intake/index.html".</summary>
    public string Url(string relativePath, string? query = null) =>
        $"http://127.0.0.1:{Port}/{relativePath.TrimStart('/')}?ccp_t={Token}" + (string.IsNullOrEmpty(query) ? "" : "&" + query);

    /// <summary>The URL a page loads a library file by (WPF DtrhAssetManifest.AssetUrl's https://ccp.assets/&lt;rel&gt;):
    /// same origin as the page, so the token cookie covers it; each path segment escaped.</summary>
    public string AssetUrl(string rel) => $"http://127.0.0.1:{Port}" + AssetPath(rel);

    /// <summary><see cref="AssetUrl"/> without the origin ("/ccp.assets/images/a.png"): what DtRH's hostMedia.js
    /// counts as local (its LOCAL_URL_RE knows https://ccp.*/ and root-relative urls, not 127.0.0.1), so the
    /// page's WebGL layer may draw it.</summary>
    public static string AssetPath(string rel) =>
        "/" + AssetsPrefix + string.Join('/', rel.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

    /// <summary>Per-port so two runs on 127.0.0.1 (cookies ignore the port) never overwrite each other.</summary>
    string CookieName => $"ccp_t_{Port}";

    bool IsToken(string? candidate) =>
        candidate != null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(Token));

    async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) { return; } // listener stopped
            // A relayed Scrolller fetch can take minutes: it waits on its own slots, never on the page's files, and the
            // slot is taken INSIDE the task, so nothing a request waits on ever stalls this accept loop.
            bool relay = ctx.Request.Url?.AbsolutePath.StartsWith(ScrolllerRelay.PathPrefix, StringComparison.Ordinal) == true;
            _ = Task.Run(() => HandleAsync(ctx, relay));
        }
    }

    /// <summary>How long a request waits for a free slot before it is answered 503 + Retry-After.</summary>
    internal TimeSpan SlotWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cancelled by <see cref="Dispose"/>: every queued request and every relay fetch stops with the server.</summary>
    readonly CancellationTokenSource _closing = new();

    async Task HandleAsync(HttpListenerContext ctx, bool relay)
    {
        var slots = relay ? _relayInFlight : _inFlight;
        bool held = false;
        try
        {
            held = await slots.WaitAsync(SlotWait, _closing.Token).ConfigureAwait(false);
            if (!held)
            {
                ctx.Response.StatusCode = 503;
                ctx.Response.Headers.Add("Retry-After", "2");
                return;
            }
            if (relay) await ServeRelayAsync(ctx).ConfigureAwait(false); else Serve(ctx);
        }
        catch (Exception ex) { Log.Debug(ex, "WebAssetServer: request failed"); }
        finally
        {
            try { ctx.Response.Close(); } catch { }
            if (held) slots.Release();
        }
    }

    /// <summary>The host, token, loopback and method rule every answer passes; false = refused (status set).</summary>
    bool Admit(HttpListenerContext ctx, out bool queryToken)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        queryToken = IsToken(req.QueryString["ccp_t"]);
        bool cookieToken = IsToken(req.Cookies[CookieName]?.Value);
        // A foreign Host header (DNS rebinding): the managed (Linux) listener 404s it against the
        // 127.0.0.1 prefix, but Windows' http.sys routes it here (CI answered 200), so refuse it alike.
        if (!string.Equals(req.UserHostName, $"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
        {
            res.StatusCode = 404;
            return false;
        }
        if (!IPAddress.IsLoopback(req.RemoteEndPoint.Address)
            || !(queryToken || cookieToken)
            || (req.HttpMethod != "GET" && req.HttpMethod != "HEAD"))
        {
            res.StatusCode = 403;
            return false;
        }
        if (queryToken)
            res.Headers.Add("Set-Cookie", $"{CookieName}={Token}; Path=/; HttpOnly; SameSite=Strict");
        return true;
    }

    void Serve(HttpListenerContext ctx)
    {
        if (!Admit(ctx, out _)) return;
        var path = ResolveFile(ctx.Request.Url!.AbsolutePath);
        if (path == null) { ctx.Response.StatusCode = 404; return; }
        // A Scrolller copy (DtRH's .temp/ccp_temp_remote_*) is no-store: the engine's disk cache must never keep a
        // second copy that outlives the copy's 24 h or the off switch (licence 3.4).
        SendFile(ctx, path, Types.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream"), CacheControlFor(path));
    }

    /// <summary>Slots for relayed Scrolller requests (a fresh fetch holds one until the download lands).</summary>
    readonly SemaphoreSlim _relayInFlight = new(6);

    /// <summary>Test seams for the relay: the cached copy, the share rule, and the device cache's read-or-fetch
    /// (honest user-agent, charged as bytes arrive, born at download for the 24 h sweep).</summary>
    internal Func<string, string?> ScrolllerCached { get; init; } = ScrolllerDiskCache.TryGetPath;
    internal Func<string, bool> ScrolllerMayFetchFresh { get; init; } = ScrolllerPolicyService.MayFetchFresh;
    internal Func<string, CancellationToken, Task<string?>> ScrolllerFetch { get; init; } =
        (url, ct) => ScrolllerDiskCache.GetOrFetchAsync(url, ScrolllerItemIndex.MetaFor(url), ct);

    static readonly TimeSpan RelayFetchBudget = TimeSpan.FromMinutes(3);

    /// <summary>
    /// <c>/scrolller-media/&lt;sub&gt;/&lt;path&gt;</c>: the page gate for Scrolller media (docs/avalonia-decisions.md
    /// "scrolller-web-gate", Core <see cref="ScrolllerRelay"/>). Same admission as every page file; 404 for anything that
    /// is not Scrolller media (never an open proxy); 403 while the switch is off or when nothing is cached and no fresh
    /// fetch is allowed; otherwise the device cache's copy, with Range, always no-store.
    /// </summary>
    async Task ServeRelayAsync(HttpListenerContext ctx)
    {
        if (!Admit(ctx, out _)) return;
        var res = ctx.Response;
        var url = ScrolllerRelay.FromRelayPath(ctx.Request.RawUrl);
        var cached = url == null || !ScrolllerPolicy.Enabled ? null : ScrolllerCached(url);
        // HEAD: headers of a cached copy only; an uncached url is a 404, never a fetch (and never a charge).
        bool head = ctx.Request.HttpMethod == "HEAD";
        var route = ScrolllerRelay.Decide(url, ScrolllerPolicy.Enabled, cached != null,
            url != null && !head && ScrolllerMayFetchFresh(url), head);
        string? path = cached;
        switch (route)
        {
            case ScrolllerRelayRoute.NotFound: res.StatusCode = 404; return;
            case ScrolllerRelayRoute.Block:
            case ScrolllerRelayRoute.Refuse: res.StatusCode = 403; return;
            case ScrolllerRelayRoute.FetchThenServe:
                // Bounded by the budget and by the server's life. HttpListener gives no signal for a client that leaves
                // before the first byte is written, so a page that goes away mid-fetch is noticed at the write below
                // (it throws, the answer ends); the fetch itself lands in the device cache like WPF's whole fetch.
                using (var budget = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token))
                {
                    budget.CancelAfter(RelayFetchBudget);
                    try { path = await ScrolllerFetch(url!, budget.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { path = null; }
                }
                if (_closing.IsCancellationRequested) { res.Abort(); return; }
                break;
        }
        if (path == null || !File.Exists(path)) { res.StatusCode = 502; return; }
        SendFile(ctx, path, ScrolllerRelay.ContentTypeFor(url!), "no-store");
    }

    /// <summary>Streams one file (or the one byte range asked for) with the house headers.</summary>
    void SendFile(HttpListenerContext ctx, string path, string contentType, string cacheControl)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.ContentType = contentType;
        res.Headers.Add("Cache-Control", cacheControl);
        res.Headers.Add("X-Content-Type-Options", "nosniff");
        res.Headers.Add("Referrer-Policy", "no-referrer");
        res.Headers.Add("Accept-Ranges", "bytes");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long from = 0, to = file.Length - 1;
        // One bytes=a-b / a- / -n range (what a media element asks for to seek, as WebView2's virtual host
        // answers it): 206 with just that slice, 416 when it lies past the end; anything else gets the whole file.
        if (ParseRange(req.Headers["Range"], file.Length) is { } r)
        {
            if (r.From < 0) { res.StatusCode = 416; res.Headers.Add("Content-Range", $"bytes */{file.Length}"); return; }
            (from, to) = r;
            res.StatusCode = 206;
            res.Headers.Add("Content-Range", $"bytes {from}-{to}/{file.Length}");
        }
        res.ContentLength64 = to - from + 1;
        if (req.HttpMethod != "GET") return;
        file.Position = from;
        var buf = new byte[81920];
        for (long left = to - from + 1; left > 0;)
        {
            int n = file.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0) break;
            res.OutputStream.Write(buf, 0, n);
            left -= n;
        }
    }

    /// <summary>The Cache-Control a served file gets: no-store for a remote copy, else no-cache.</summary>
    internal static string CacheControlFor(string path) => DtrhRemoteMedia.IsCopyFile(path) ? "no-store" : "no-cache";

    /// <summary>A single byte range, clamped to the file; (-1,-1) = unsatisfiable; null = absent or not one range.</summary>
    internal static (long From, long To)? ParseRange(string? header, long length)
    {
        if (header == null || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        var spec = header[6..].Trim();
        int dash = spec.IndexOf('-');
        if (dash < 0 || spec.Contains(',')) return null;
        string a = spec[..dash].Trim(), b = spec[(dash + 1)..].Trim();
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var tail) || tail < 0) return null;
            return length == 0 || tail == 0 ? (-1, -1) : (Math.Max(0, length - tail), length - 1);
        }
        if (!long.TryParse(a, out var from) || from < 0) return null;
        long to = length - 1;
        if (b.Length > 0 && (!long.TryParse(b, out to) || to < from)) return null;
        if (from >= length) return (-1, -1);
        return (from, Math.Min(to, length - 1));
    }

    /// <summary>URL prefix for the user's asset library, WPF's second virtual host <c>https://ccp.assets/</c>
    /// (IntakeHostService maps it over App.EffectiveAssetsPath): same server, same token rule.</summary>
    public const string AssetsPrefix = "ccp.assets/";

    /// <summary>Root behind <see cref="AssetsPrefix"/>, read per request (the user can move the library);
    /// null = the prefix is not served.</summary>
    public Func<string?>? AssetsRoot { get; init; }

    /// <summary>Downloaded audio-web mirror; read on every request so a pack installed during a run is available.</summary>
    public Func<string?>? ContentRoot { get; init; }

    /// <summary>The file a URL path names, or null if it is missing or lies outside its root, symlinks followed.</summary>
    internal string? ResolveFile(string urlPath)
    {
        var rel = Uri.UnescapeDataString(urlPath).TrimStart('/');
        var root = _root;
        bool asset = rel.StartsWith(AssetsPrefix, StringComparison.Ordinal);
        if (asset)
        {
            if (AssetsRoot?.Invoke() is not { Length: > 0 } assets) return null;
            root = Path.GetFullPath(assets).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            rel = rel[AssetsPrefix.Length..];
            // Media only, never the app's own dot-folders (.temp, .packs), never the profile itself.
            if (!MediaTypeSniffer.MediaExtensions.Contains(Path.GetExtension(rel))
                || (rel.Split('/', '\\').Any(seg => seg.StartsWith('.')) && !IsRemoteCopy(rel))
                || HoldsUserData(root)) return null;
        }
        if (rel.Length == 0 || rel.EndsWith('/')) rel += "index.html";
        if (rel.Contains('\0')) return null;
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (!Inside(full, root)) return null;
        if (!File.Exists(full))
        {
            if (asset || !ContentLocator.IsIntakeWebAudio(rel) || ContentRoot?.Invoke() is not { Length: > 0 } content)
                return null;
            root = Path.GetFullPath(content).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (HoldsUserData(root)) return null;
            // ponytail: the page's first audio URL uses its own origin; resolve the pack mirror here
            // so the same cookie and range handling serve recorded clips, with install-dir priority.
            full = Path.GetFullPath(Path.Combine(root, rel));
            if (!Inside(full, root) || !File.Exists(full)) return null;
        }
        if (!LinksStayInside(full, root)) return null;
        if (asset && !IntakeRun.IsAssetActive(IntakeRun.DisabledAssetSet(DisabledAssets()), root, full)) return null;
        return full;
    }

    /// <summary>The one dot-folder file served: DtRH's copy of an online entry, <c>.temp/ccp_temp_remote_*</c>
    /// (Core DtrhRemoteMedia; WPF's ccp.assets maps the whole folder). Pack decrypts there stay unserved.</summary>
    static bool IsRemoteCopy(string rel)
    {
        var seg = rel.Split('/', '\\');
        return seg.Length == 2 && seg[0] == ".temp" && DtrhRemoteMedia.IsCopyFile(seg[1]);
    }

    /// <summary>The user's unchecked assets (Settings DisabledAssetPaths), never served.</summary>
    public Func<IEnumerable<string>?> DisabledAssets { get; init; } = () => CoreSettings.Current.DisabledAssetPaths;

    int _userDataWarned;

    /// <summary>An assets root that is, or contains, the profile folder would expose settings and secrets.</summary>
    bool HoldsUserData(string root)
    {
        var data = Path.GetFullPath(CorePaths.UserData).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!data.StartsWith(root, StringComparison.Ordinal)) return false;
        if (Interlocked.Exchange(ref _userDataWarned, 1) == 0)
            Log.Warning("WebAssetServer: refusing to serve assets from {Root}: it holds the profile folder", root);
        return true;
    }

    static bool Inside(string full, string root) => full.StartsWith(root, StringComparison.Ordinal);

    /// <summary>Every link on the way from the root down to the file must land inside the root.</summary>
    static bool LinksStayInside(string full, string root)
    {
        for (var p = full; p.Length > root.Length; p = Path.GetDirectoryName(p)!)
        {
            FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
            if (info.LinkTarget != null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target
                && !Inside(Path.GetFullPath(target.FullName), root)
                && Path.GetFullPath(target.FullName) + Path.DirectorySeparatorChar != root)
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        try { _closing.Cancel(); } catch (ObjectDisposedException) { }
        _listener.Close();
    }
}
