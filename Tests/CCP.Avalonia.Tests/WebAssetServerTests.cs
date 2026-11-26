using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using CCP.Testing;
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

    static HttpClient Client(bool cookies = true) =>
        new(new HttpClientHandler { UseCookies = cookies, CookieContainer = new CookieContainer() });

    /// <summary>The For You feed seeks a clip window inside each video (fyp/surfaces.js): a media element asks for a
    /// byte range and must get only that slice (206), never the whole file.</summary>
    [Fact]
    public async Task M4v_is_served_as_video_for_the_browser_gate()
    {
        var (server, root) = Make();
        using (server)
        using (var http = Client())
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4"), Path.Combine(root, "clip.m4v"));
            var response = await http.GetAsync(server.Url("clip.m4v"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("video/mp4", response.Content.Headers.ContentType!.MediaType);
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        }
        Directory.Delete(Path.GetDirectoryName(root)!, true);
    }

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
        Symlinks.RequireOrSkip();
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

    /// <summary>ccp-bugs#1703: a library folder linked from elsewhere (FypAssetManifest.Walk follows it) must play in
    /// For You, as WPF's WebView2 folder mapping follows links (FypHostService.cs:96). Only media, never the profile
    /// or a dot-folder, and still behind the token.</summary>
    [Fact]
    public async Task AssetsServeMediaThroughALinkedFolderOutsideTheRoot_ButNotSecrets()
    {
        Symlinks.RequireOrSkip();
        var web = Directory.CreateTempSubdirectory("fyp-web-").FullName;
        var assets = Directory.CreateTempSubdirectory("fyp-assets-").FullName;
        var outside = Directory.CreateTempSubdirectory("fyp-outside-").FullName;
        var profileClip = Path.Combine(CorePaths.UserData, "leak.mp4");
        try
        {
            File.WriteAllText(Path.Combine(outside, "clip.mp4"), "clip");
            File.WriteAllText(Path.Combine(outside, "notes.txt"), "secret");
            Directory.CreateDirectory(Path.Combine(outside, ".hidden"));
            File.WriteAllText(Path.Combine(outside, ".hidden", "h.mp4"), "secret");
            Directory.CreateDirectory(CorePaths.UserData);
            File.WriteAllText(profileClip, "secret");
            Directory.CreateSymbolicLink(Path.Combine(assets, "videos"), outside);
            File.CreateSymbolicLink(Path.Combine(outside, "notes.mp4"), Path.Combine(outside, "notes.txt"));
            File.CreateSymbolicLink(Path.Combine(outside, "hidden.mp4"), Path.Combine(outside, ".hidden", "h.mp4"));
            File.CreateSymbolicLink(Path.Combine(outside, "profile.mp4"), profileClip);

            using var server = new WebAssetServer(web) { AssetsRoot = () => assets, DisabledAssets = () => null };
            using var http = Client();
            var url = server.AssetUrl("videos/clip.mp4");
            using (var stranger = Client())
                Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(url)).StatusCode);
            var response = await http.GetAsync(url + "?ccp_t=" + server.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("clip", await response.Content.ReadAsStringAsync());
            string P(string rel) => "/" + WebAssetServer.AssetsPrefix + rel;
            Assert.Null(server.ResolveFile(P("videos/notes.txt")));     // not media
            Assert.Null(server.ResolveFile(P("videos/notes.mp4")));     // media name, non-media target
            Assert.Null(server.ResolveFile(P("videos/hidden.mp4")));    // target in a dot-folder
            Assert.Null(server.ResolveFile(P("videos/profile.mp4")));   // target in the profile
        }
        finally
        {
            File.Delete(profileClip);
            Directory.Delete(web, true);
            Directory.Delete(assets, true);
            Directory.Delete(outside, true);
        }
    }

    /// <summary>A link target with <c>..</c> after a folder link: the OS follows the folder link first, so
    /// <c>s/../secret.txt</c> leaves the root even though the text collapses to a path inside it.</summary>
    [Fact]
    public void AssetLinkWithDotDotAfterAFolderLink_DoesNotEscapeTheRoot()
    {
        Symlinks.RequireOrSkip();
        var web = Directory.CreateTempSubdirectory("fyp-web-").FullName;
        var assets = Directory.CreateTempSubdirectory("fyp-assets-").FullName;
        var outside = Directory.CreateTempSubdirectory("fyp-outside-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(outside, "inner"));
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
            Directory.CreateDirectory(Path.Combine(assets, "videos"));
            Directory.CreateSymbolicLink(Path.Combine(assets, "videos", "s"), Path.Combine(outside, "inner"));
            // The target uses the platform separator: Windows reads a '/' inside a link target as part of a
            // name ("The filename, directory name, or volume label syntax is incorrect").
            File.CreateSymbolicLink(Path.Combine(assets, "videos", "a.mp4"), Path.Combine("s", "..", "secret.txt"));
            // POSIX follows s first, so the OS does escape. Windows may collapse the '..' in the target text
            // before it follows s, so the escape itself is pinned on POSIX only; the server refuses it on both.
            if (!OperatingSystem.IsWindows())
                Assert.Equal("secret", File.ReadAllText(Path.Combine(assets, "videos", "a.mp4")));

            using var server = new WebAssetServer(web) { AssetsRoot = () => assets, DisabledAssets = () => null };
            Assert.Null(server.ResolveFile("/" + WebAssetServer.AssetsPrefix + "videos/a.mp4"));
        }
        finally
        {
            Directory.Delete(web, true);
            Directory.Delete(assets, true);
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task WhisperHosts_ServeOnlyAudioWithToken_AndRefuseTraversalAndLinks()
    {
        var dir = Directory.CreateTempSubdirectory("pbp-audio-").FullName;
        try
        {
            var audio = Directory.CreateDirectory(Path.Combine(dir, "audio")).FullName;
            File.WriteAllText(Path.Combine(audio, "a one.mp3"), "clip");
            File.WriteAllText(Path.Combine(audio, "words.json"), "manifest");
            File.WriteAllText(Path.Combine(dir, "outside.mp3"), "outside");
            using var server = new WebAssetServer(dir) { SubAudioRoot = () => audio, WordsRoot = () => audio };
            using var http = Client();
            foreach (var host in new[] { "ccp.subaudio", "ccp.words" })
            {
                var url = server.WhisperUrl($"https://{host}/a%20one.mp3");
                using var first = Client();
                Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync(url)).StatusCode);
                var response = await http.GetAsync(url + "?ccp_t=" + server.Token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("audio/mpeg", response.Content.Headers.ContentType!.MediaType);
                Assert.Equal("clip", await response.Content.ReadAsStringAsync());
                Assert.Null(server.ResolveFile($"/{host}/words.json"));
                Assert.Null(server.ResolveFile($"/{host}/%2e%2e/outside.mp3"));
                // A new client proves each host still needs authentication after a cookie-bearing request.
                using var unauth = Client();
                Assert.Equal(HttpStatusCode.Forbidden, (await unauth.GetAsync(url)).StatusCode);
            }
            Symlinks.RequireOrSkip();   // last, so the token and traversal checks above always run
            File.CreateSymbolicLink(Path.Combine(audio, "link.mp3"), Path.Combine(dir, "outside.mp3"));
            Assert.Null(server.ResolveFile("/ccp.words/link.mp3"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ArtPrefix_ServesShippedImagesWithCookie_AndConfinesPaths()
    {
        using var s = Make().server;
        using var http = Client();
        var origin = $"http://127.0.0.1:{s.Port}/";
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(origin + "__art__/boons/afterglow.png")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(s.Url("game/index.html"))).StatusCode);
        foreach (var rel in new[] { "boons/afterglow.png", "biomes/casino.png", "bubbles/gold_droplet.png", "guide/intro.png" })
        {
            var expected = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", rel));
            var response = await http.GetAsync(origin + "__art__/" + rel);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
        }
        Assert.Null(s.ResolveFile("/__art__/%2e%2e/secret.txt"));
        Assert.Null(s.ResolveFile("/__art__/missing.png"));
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
