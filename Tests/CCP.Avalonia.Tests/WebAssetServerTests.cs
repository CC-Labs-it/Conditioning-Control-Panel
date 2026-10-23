using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebAssetServerTests
{
    static (WebAssetServer server, string root) Make()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-web-").FullName;
        var root = Path.Combine(dir, "web");
        Directory.CreateDirectory(Path.Combine(root, "game"));
        File.WriteAllText(Path.Combine(root, "game", "index.html"), "<p>hi</p>");
        File.WriteAllText(Path.Combine(dir, "secret.txt"), "outside");
        return (new WebAssetServer(root), root);
    }

    [Fact]
    public async Task RaceAudioPackIsServedWithTokenAndRanges()
    {
        using var server = Make().server;
        var relative = "Resources/web/dtrh/assets/bubbles/sfx/race-test-" + Guid.NewGuid().ToString("N") + ".mp3";
        var packed = Path.Combine(ConditioningControlPanel.Services.ContentLocator.ContentRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(packed)!);
        await File.WriteAllBytesAsync(packed, new byte[] { 1, 2, 3, 4 });
        try
        {
            using var http = Client(false);
            var pagePath = relative["Resources/web/".Length..];
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"http://127.0.0.1:{server.Port}/" + pagePath)).StatusCode);
            using var req = new HttpRequestMessage(HttpMethod.Get, server.Url(pagePath));
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1, 2);
            using var response = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("audio/mpeg", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(new byte[] { 2, 3 }, await response.Content.ReadAsByteArrayAsync());
            Assert.Null(server.ResolveFile("/dtrh/assets/../../settings.json"));
        }
        finally { File.Delete(packed); }
    }

    [Fact]
    public void RaceSpiralRouteServesOnlyWhitelistedGifs()
    {
        using var server = Make().server;
        var store = ConditioningControlPanel.Services.Chaos.DtrhLoomStore.SpiralsFolder;
        Directory.CreateDirectory(store);
        var gif = Path.Combine(store, "loom_race-route-test.gif");
        File.WriteAllBytes(gif, new byte[] { 1 });
        try
        {
            Assert.Equal(gif, server.ResolveFile("/ccp.spirals/loom_race-route-test.gif"));
            Assert.Null(server.ResolveFile("/ccp.spirals/loom_race-route-test.json"));
            Assert.Null(server.ResolveFile("/ccp.spirals/loom_../settings.gif"));
            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(store, "loom_race-route-link.gif");
                var outside = Path.GetFullPath(Path.Combine(store, "..", "race-route-outside.gif"));
                File.WriteAllBytes(outside, new byte[] { 1 });
                File.CreateSymbolicLink(link, outside);
                try { Assert.Null(server.ResolveFile("/ccp.spirals/loom_race-route-link.gif")); }
                finally { File.Delete(link); File.Delete(outside); }
            }
        }
        finally { File.Delete(gif); }
    }

    static HttpClient Client(bool cookies = true) =>
        new(new HttpClientHandler { UseCookies = cookies, CookieContainer = new CookieContainer() });

    /// <summary>The For You feed seeks a clip window inside each video (fyp/surfaces.js): a media element asks for a
    /// byte range and must get only that slice (206), never the whole file.</summary>
    [Fact]
    public async Task ServesOneByteRange_AsWebView2DoesForSeekingMedia()
    {
        using var s = Make().server;
        using var http = Client();
        async Task<HttpResponseMessage> Get(string? range, string method = "GET")
        {
            var req = new HttpRequestMessage(new HttpMethod(method), s.Url("game/index.html"));
            if (range != null) req.Headers.TryAddWithoutValidation("Range", range);
            return await http.SendAsync(req);
        }
        var mid = await Get("bytes=3-6");                  // "<p>hi</p>" -> "hi</"
        Assert.Equal(HttpStatusCode.PartialContent, mid.StatusCode);
        Assert.Equal("hi</", await mid.Content.ReadAsStringAsync());
        Assert.Equal("bytes 3-6/9", mid.Content.Headers.ContentRange!.ToString());
        Assert.Equal("p>", await (await Get("bytes=-2")).Content.ReadAsStringAsync());
        Assert.Equal("</p>", await (await Get("bytes=5-")).Content.ReadAsStringAsync());
        var past = await Get("bytes=50-");
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, past.StatusCode);
        Assert.Equal("bytes */9", past.Content.Headers.GetValues("Content-Range").Single());
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, (await Get("bytes=-0")).StatusCode);   // RFC 7233 2.1
        // A backward range and a multi-range request are ignored: the whole file, 200.
        foreach (var ignored in new[] { "bytes=6-3", "bytes=0-1,3-4" })
        {
            var r = await Get(ignored);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("<p>hi</p>", await r.Content.ReadAsStringAsync());
        }
        var head = await Get("bytes=3-6", "HEAD");
        Assert.Equal(HttpStatusCode.PartialContent, head.StatusCode);
        Assert.Equal(4, head.Content.Headers.ContentLength);
        var whole = await Get(null);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal("bytes", Assert.Single(whole.Headers.AcceptRanges));
    }

    [Fact]
    public async Task ServesWithToken_AndCookieCarriesLaterLoads()
    {
        using var s = Make().server;
        using var http = Client();
        var first = await http.GetAsync(s.Url("game/index.html"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("text/html", first.Content.Headers.ContentType!.MediaType);
        Assert.Equal("<p>hi</p>", await first.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", Assert.Single(first.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(first.Headers.GetValues("Referrer-Policy")));
        Assert.StartsWith($"ccp_t_{s.Port}=", Assert.Single(first.Headers.GetValues("Set-Cookie")));
        // A root-relative load from the page carries no query: the cookie must authorise it.
        var second = await http.GetAsync($"http://127.0.0.1:{s.Port}/game/");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task RefusesWithoutToken_WrongToken_AndForeignHost()
    {
        using var s = Make().server;
        using var http = Client(cookies: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"http://127.0.0.1:{s.Port}/game/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"http://127.0.0.1:{s.Port}/game/index.html?ccp_t=00")).StatusCode);
        var rebind = new HttpRequestMessage(HttpMethod.Get, s.Url("game/index.html"));
        rebind.Headers.Host = "evil.example";
        // HttpListener matches the 127.0.0.1 prefix on the Host header and answers 404 for any other name.
        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(rebind)).StatusCode);
    }

    [Fact]
    public void NeverResolvesOutsideRoot()
    {
        var (s, root) = Make();
        using (s)
        {
            Assert.Equal(Path.Combine(root, "game", "index.html"), s.ResolveFile("/game/index.html"));
            Assert.Null(s.ResolveFile("/../secret.txt"));
            Assert.Null(s.ResolveFile("/%2e%2e/secret.txt"));
            Assert.Null(s.ResolveFile("/game/%2e%2e/%2e%2e/secret.txt"));
            Assert.Null(s.ResolveFile("/" + Path.Combine(Path.GetDirectoryName(root)!, "secret.txt")));
        }
    }

    [Fact]
    public void RefusesSymlinksThatLeaveTheRoot()
    {
        var (s, root) = Make();
        using (s)
        {
            var outside = Path.Combine(Path.GetDirectoryName(root)!, "secret.txt");
            File.CreateSymbolicLink(Path.Combine(root, "leak.txt"), outside);
            Directory.CreateSymbolicLink(Path.Combine(root, "up"), Path.GetDirectoryName(root)!);
            File.CreateSymbolicLink(Path.Combine(root, "ok.html"), Path.Combine(root, "game", "index.html"));
            Assert.Null(s.ResolveFile("/leak.txt"));
            Assert.Null(s.ResolveFile("/up/secret.txt"));
            Assert.NotNull(s.ResolveFile("/ok.html"));
        }
    }

    [Fact]
    public async Task SharedServer_ServesTheShippedIntakePage()
    {
        using var http = Client();
        var res = await http.GetAsync(WebAssetServer.Shared.Url("intake/index.html"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("<html", (await res.Content.ReadAsStringAsync()).ToLowerInvariant());
    }
}
