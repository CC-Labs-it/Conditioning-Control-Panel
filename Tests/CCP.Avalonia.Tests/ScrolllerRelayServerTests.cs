using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The loopback Scrolller relay (WebAssetServer /scrolller-media/, Core ScrolllerRelay; docs/avalonia-decisions.md
/// "scrolller-web-gate"): the token rule like every page file, 403 while the switch is off or the share is spent,
/// cached copies served with Range and no-store, a fresh copy only through the device cache, never an open proxy;
/// and the hosts rewrite Scrolller urls in every frame they post.
/// </summary>
[Collection(ScrolllerRelayCollection.Name)]   // ScrolllerPolicy is process-wide
public sealed class ScrolllerRelayServerTests : IDisposable
{
    private const string Url = "https://images.scrolller.com/abc/clip-9.jpg";
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-relay-").FullName;
    private int _fetches;

    public void Dispose()
    {
        ScrolllerPolicy.Apply(true, null);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private WebAssetServer Make(string? cached, bool fresh, string? fetched = null)
    {
        var web = Path.Combine(_dir, "web");
        Directory.CreateDirectory(web);
        File.WriteAllText(Path.Combine(web, "index.html"), "<p>hi</p>");
        return new WebAssetServer(web)
        {
            ScrolllerCached = u => u == Url ? cached : null,
            ScrolllerMayFetchFresh = _ => fresh,
            ScrolllerFetch = (u, _) => { Interlocked.Increment(ref _fetches); return Task.FromResult(u == Url ? fetched : null); },
        };
    }

    private string File10(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, Enumerable.Range(0, 10).Select(i => (byte)i).ToArray());
        return p;
    }

    private static async Task<HttpClient> Signed(WebAssetServer s)
    {
        var http = new HttpClient(new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() });
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(s.Url("index.html"))).StatusCode);   // the token cookie
        return http;
    }

    private static string Relay(WebAssetServer s) => s.RelayUrl(Url)!;

    [Fact]
    public async Task A_cached_copy_is_served_with_range_and_no_store_and_never_fetched()
    {
        using var s = Make(File10("c.jpg"), fresh: false);
        using var http = await Signed(s);
        var whole = await http.GetAsync(Relay(s));
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal("image/jpeg", whole.Content.Headers.ContentType!.MediaType);
        Assert.Equal(10, (await whole.Content.ReadAsByteArrayAsync()).Length);
        Assert.Contains("no-store", whole.Headers.CacheControl!.ToString());
        var req = new HttpRequestMessage(HttpMethod.Get, Relay(s));
        req.Headers.TryAddWithoutValidation("Range", "bytes=2-4");
        var part = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(new byte[] { 2, 3, 4 }, await part.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, _fetches);
    }

    [Fact]
    public async Task Fresh_media_comes_through_the_device_cache_only_when_allowed()
    {
        using (var s = Make(null, fresh: true, fetched: File10("f.jpg")))
        using (var http = await Signed(s))
        {
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(Relay(s))).StatusCode);
            Assert.Equal(1, _fetches);
        }
        using (var s = Make(null, fresh: false, fetched: File10("g.jpg")))
        using (var http = await Signed(s))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(Relay(s))).StatusCode);   // share spent
            Assert.Equal(1, _fetches);
        }
    }

    [Fact]
    public async Task The_off_switch_refuses_even_a_cached_copy()
    {
        using var s = Make(File10("c.jpg"), fresh: true);
        using var http = await Signed(s);
        ScrolllerPolicy.Apply(false, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(Relay(s))).StatusCode);
        Assert.Equal(0, _fetches);
    }

    [Fact]
    public async Task No_token_no_media_and_never_an_open_proxy()
    {
        using var s = Make(File10("c.jpg"), fresh: true);
        using (var bare = new HttpClient(new HttpClientHandler { UseCookies = false }))
            Assert.Equal(HttpStatusCode.Forbidden, (await bare.GetAsync(s.RelayOrigin + ScrolllerRelay.ToRelayPath(Url))).StatusCode);   // no token in the path
        using var http = await Signed(s);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(s.RelayOrigin + ScrolllerRelay.WithToken(ScrolllerRelay.ToRelayPath(Url)!, "00"))).StatusCode);
        // The page origin never relays (its sockets are the pages' own), even with the page cookie.
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{s.Port}{ScrolllerRelay.ToRelayPath(Url)}")).StatusCode);
        foreach (var path in new[] { "/scrolller-media/api/v2/graphql.jpg", "/scrolller-media/images/page.html", "/scrolller-media/evil.test/a.jpg" })
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(s.RelayOrigin + ScrolllerRelay.WithToken(path, s.Token))).StatusCode);
        Assert.Equal(0, _fetches);
    }

    [Fact]
    public void Every_host_post_seam_rewrites_scrolller_urls()
    {
        var root = RepoRoot();
        foreach (var (file, seam) in new[]
        {
            ("Views/Games/GameWindow.cs", "ScrolllerRelay.RewriteJson(JsonConvert.SerializeObject(message), ScrolllerRelay.LoopbackOrigin(PageUrl))"),
            ("Views/Windows/IntakeHostWindow.cs", "ScrolllerRelay.RewriteJson(JsonConvert.SerializeObject(message), ScrolllerRelay.LoopbackOrigin(PageUrl))"),
            ("Views/Controls/WebHost.axaml.cs", "ScrolllerRelay.RewriteJson(json, origin)"),
        })
            Assert.Contains(seam, File.ReadAllText(Path.Combine(root, "CCP.Avalonia", file)));
    }

    [Fact]
    public void No_page_this_head_hosts_fetches_scrolller_itself()
    {
        // The relay only covers urls the HOST posts. A page that calls the CDN or the API on its own would go out with
        // the engine's user-agent, uncharged and uncached. Goon and Arcademy do (web shims), and have no host here yet.
        var web = Path.Combine(RepoRoot(), "Assets", "web");
        var hosted = new[] { "fyp", "intake", "dtrh", "piecebypiece", "first-show", "player", "remix", "tunnel", "chaster",
            "emergency-exit", "codex", Path.Combine("backroom", "stations", "breakout") };
        var offenders = hosted.Select(d => Path.Combine(web, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".js") || f.EndsWith(".mjs") || f.EndsWith(".html"))
            .Where(f => !f.Contains("test", StringComparison.OrdinalIgnoreCase) && !f.Contains("smoke", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("https://api.scrolller.com") || File.ReadAllText(f).Contains("https://images.scrolller.com"))
            .Select(f => Path.GetRelativePath(web, f)).ToList();
        Assert.Empty(offenders);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}

[CollectionDefinition(Name)]
public sealed class ScrolllerRelayCollection { public const string Name = "ScrolllerRelay"; }
