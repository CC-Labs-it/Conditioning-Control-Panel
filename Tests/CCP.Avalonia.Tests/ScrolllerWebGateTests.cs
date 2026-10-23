using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Avalonia.Platform;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Scrolller gate on every web view of this head (WPF ScrolllerWebGate on every WebView2 core): the Windows routing
/// rule (the relay's rule, so media always comes from the device cache), the answers it makes from a cached file, the
/// Linux block rule, and the source scan that keeps a new web view from shipping without the gate.
/// </summary>
public sealed class ScrolllerWebGateTests : IDisposable
{
    private const string Media = "https://images.scrolller.com/abc/clip-9.mp4";
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-gate-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("https://cclabs.app/a.jpg", "GET", true, false, true, (int)ScrolllerGateRoute.Pass)]
    [InlineData("https://notscrolller.com/a.jpg", "GET", true, false, true, (int)ScrolllerGateRoute.Pass)]
    [InlineData(Media, "GET", false, true, true, (int)ScrolllerGateRoute.Block)]                      // off beats a cached copy
    [InlineData("https://api.scrolller.com/api/v2/graphql", "POST", false, false, true, (int)ScrolllerGateRoute.Block)]
    [InlineData("https://api.scrolller.com/api/v2/graphql", "POST", true, false, true, (int)ScrolllerGateRoute.Rewrite)]
    [InlineData("https://scrolller.com/r/hypno", "GET", true, false, true, (int)ScrolllerGateRoute.Rewrite)]   // the site, not media
    [InlineData(Media, "HEAD", true, false, true, (int)ScrolllerGateRoute.Rewrite)]
    [InlineData(Media, "GET", true, true, false, (int)ScrolllerGateRoute.ServeCached)]                // cached beats a spent share
    [InlineData(Media, "GET", true, false, true, (int)ScrolllerGateRoute.FetchThenServe)]             // never streamed uncached
    [InlineData(Media, "GET", true, false, false, (int)ScrolllerGateRoute.Refuse)]
    public void Route_matrix(string url, string method, bool enabled, bool cached, bool fresh, int want)
        => Assert.Equal((ScrolllerGateRoute)want, ScrolllerWebView2Gate.Route(url, method, enabled, cached, fresh));

    [Fact]
    public void An_extensionless_url_loaded_as_media_is_media_but_the_api_never_is()
    {
        const string bare = "https://images.scrolller.com/abc/clip";
        Assert.Equal(ScrolllerGateRoute.Rewrite, ScrolllerWebView2Gate.Route(bare, "GET", true, false, true));
        Assert.Equal(ScrolllerGateRoute.FetchThenServe, ScrolllerWebView2Gate.Route(bare, "GET", true, false, true, mediaContext: true));
        Assert.Equal(ScrolllerGateRoute.Rewrite,
            ScrolllerWebView2Gate.Route("https://api.scrolller.com/x", "GET", true, false, true, mediaContext: true));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://cclabs.app/play", "https://cclabs.app/play")]
    [InlineData("https://app.cclabs.app/x", "https://app.cclabs.app/x")]
    [InlineData("http://127.0.0.1:5555/fyp/index.html?ccp_t=SECRET", ScrolllerWebView2Gate.OurReferer)]   // never the token
    [InlineData("https://some-site.test/", ScrolllerWebView2Gate.OurReferer)]
    public void A_foreign_referer_becomes_ours(string? page, string? want)
        => Assert.Equal(want, ScrolllerWebView2Gate.OutgoingReferer(page));

    [Fact]
    public void Slices_are_capped_like_wpf()
    {
        const long size = 100L * 1024 * 1024;
        Assert.Null(ScrolllerWebView2Gate.SliceFor(null, size));
        Assert.Equal((0L, ScrolllerWebView2Gate.SliceBytes - 1), ScrolllerWebView2Gate.SliceFor("bytes=0-", size));
        Assert.Equal((10L, 19L), ScrolllerWebView2Gate.SliceFor("bytes=10-19", size));
        Assert.Equal((0L, ScrolllerWebView2Gate.MaxBodyBytes - 1), ScrolllerWebView2Gate.SliceFor("bytes=0-99999999999", size));
        Assert.Equal((-1L, -1L), ScrolllerWebView2Gate.SliceFor("bytes=500-", 100));
    }

    [Fact]
    public void Answers_from_a_cached_file_are_no_store()
    {
        var path = Path.Combine(_dir, "c.mp4");
        File.WriteAllBytes(path, Enumerable.Range(0, 10).Select(i => (byte)i).ToArray());

        var whole = ScrolllerWebView2Gate.Open(path, Media, null);
        Assert.Equal(200, whole.Status);
        Assert.Contains("Content-Type: video/mp4", whole.Headers);
        Assert.Contains("Cache-Control: no-store", whole.Headers);
        whole.Body!.Dispose();

        var part = ScrolllerWebView2Gate.Open(path, Media, "bytes=2-4");
        Assert.Equal(206, part.Status);
        Assert.Contains("Content-Range: bytes 2-4/10", part.Headers);
        Assert.Equal(new byte[] { 2, 3, 4 }, ((MemoryStream)part.Body!).ToArray());

        Assert.Equal(416, ScrolllerWebView2Gate.Open(path, Media, "bytes=50-").Status);
        Assert.Equal(502, ScrolllerWebView2Gate.Open(Path.Combine(_dir, "gone.mp4"), Media, null).Status);
        Assert.Equal(502, ScrolllerWebView2Gate.Open(null, Media, null).Status);
    }

    [Theory]
    [InlineData("https://scrolller.com/", true)]
    [InlineData("https://scrolller.com:443/r/x", true)]
    [InlineData("https://images.scrolller.com/a/b.mp4", true)]
    [InlineData("wss://live.scrolller.com/socket", true)]
    [InlineData("https://notscrolller.com/a.jpg", false)]
    [InlineData("https://scrolller.com.evil.test/a.jpg", false)]
    [InlineData("http://127.0.0.1:5555/scrolller-media/images/a/b.jpg", false)]   // the relay is this head's own
    [InlineData("https://cclabs.app/", false)]
    public void The_linux_block_rule_covers_scrolller_and_nothing_else(string url, bool blocked)
    {
        var rules = JArray.Parse(ScrolllerWebKitFilter.RulesJson);
        var rule = Assert.Single(rules);
        Assert.Equal("block", (string?)rule["action"]!["type"]);
        var filter = (string)rule["trigger"]!["url-filter"]!;
        Assert.Equal(blocked, Regex.IsMatch(url, filter, RegexOptions.IgnoreCase));
    }

    [Fact]
    public void Every_web_view_is_a_webhost_and_every_webhost_is_gated()
    {
        var root = RepoRoot();
        var avalonia = Path.Combine(root, "CCP.Avalonia");
        var builders = Directory.EnumerateFiles(avalonia, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"new\s+(global::Avalonia\.Controls\.)?NativeWebView\s*\("))
            .Select(f => Path.GetRelativePath(avalonia, f).Replace('\\', '/')).ToList();
        Assert.Equal(new[] { "Views/Controls/WebHost.axaml.cs" }, builders);
        Assert.Contains("Platform.ScrolllerWebHostGate.Attach(_web);", File.ReadAllText(Path.Combine(avalonia, "Views", "Controls", "WebHost.axaml.cs")));
        var gate = File.ReadAllText(Path.Combine(avalonia, "Platform", "ScrolllerWebHostGate.cs"));
        Assert.Contains("ScrolllerWebView2Gate.Attach(", gate);
        Assert.Contains("ScrolllerWebKitFilter.Attach(", gate);
    }

    [Fact]
    public void A_missing_or_unknown_handle_is_not_an_error()
    {
        ScrolllerWebHostGate.OnAdapter(null);
        ScrolllerWebHostGate.OnAdapter(new global::Avalonia.Platform.PlatformHandle(IntPtr.Zero, "test"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
