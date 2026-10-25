using System;
using System.IO;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ScrolllerWebGateTests
{
    [Theory]
    [InlineData("https://images.scrolller.com/a.jpg", true)]
    [InlineData("https://images.scrolller.com./a.mp4", true)]
    [InlineData("https://notscrolller.com/a.jpg", false)]
    [InlineData("https://api.scrolller.com/a.jpg", false)]
    [InlineData("https://scrolller.com/page", false)]
    public void MediaClassificationMatchesWpf(string url, bool media) =>
        Assert.Equal(media, ScrolllerNavigationGate.IsMedia(new Uri(url)));

    [Fact]
    public Task CachedReplayIsFreeWhileAwayButSwitchOffWins() => WithCachedMedia(async (server, url) =>
    {
        ScrolllerPolicy.Away = true;
        // Simulate a fresh process with the production asynchronous cache warm-up:
        // away/spent must not turn a still-loading disk hit into a refusal.
        ScrolllerDiskCache.Flush();
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.WarmInline = false;
        var before = ScrolllerAllowance.UsedToday;
        Assert.NotNull(await ScrolllerNavigationGate.ResolveAsync(url, CancellationToken.None));
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.ScrolllerUrl(new Uri(url)));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-2");
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(3, (await response.Content.ReadAsByteArrayAsync()).Length);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(before, ScrolllerAllowance.UsedToday);
        Assert.Null(await ScrolllerNavigationGate.ResolveAsync("https://images.scrolller.com/missing.jpg", CancellationToken.None));
        ScrolllerPolicy.Apply(false, null);
        Assert.Null(await ScrolllerNavigationGate.ResolveAsync(url, CancellationToken.None));
        Assert.False(ScrolllerNavigationGate.AllowsNavigation(new Uri(url)));
        Assert.True(ScrolllerNavigationGate.AllowsNavigation(new Uri("https://example.com/")));
    });

    [Fact]
    public Task TokenRouteRefusesForeignUrlsAndPanicInvalidatesIssuedUrls() => WithCachedMedia(async (server, url) =>
    {
        using var http = new HttpClient();
        var issued = server.ScrolllerUrl(new Uri(url));
        // The issued URL works before panic, so the 403 after it is panic's doing, not a cache miss.
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(issued)).StatusCode);
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            PanicSurfaces.All.Single(s => s.Id == "scrolller-web").Stop(null);
            return Task.CompletedTask;
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(issued)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(server.ScrolllerUrl(new Uri(url)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.GetAsync(server.ScrolllerUrl(new Uri("https://example.com/a.jpg")))).StatusCode);
    });

    /// <summary>One cached Scrolller picture behind a fresh loopback server; every static restored.</summary>
    private static async Task WithCachedMedia(Func<WebAssetServer, string, Task> body)
    {
        var root = Directory.CreateTempSubdirectory("ccp-gate-").FullName;
        var oldRoot = ScrolllerDiskCache.RootProvider;
        var oldFormat = ScrolllerDiskCache.FormatCheck;
        var oldConsent = ScrolllerDiskCache.RemoteConsent;
        var oldWarm = ScrolllerDiskCache.WarmInline;
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => root;
        var enabled = ScrolllerPolicy.Enabled;
        var away = ScrolllerPolicy.Away;
        const string url = "https://images.scrolller.com/cached.jpg";
        try
        {
            ScrolllerPolicy.Apply(true, null);
            using (var writer = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta("picture")))
            {
                Assert.NotNull(writer);
                writer!.Stream.Write(new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0, 0, 0, 0 });
                writer.Commit();
            }
            using var server = new WebAssetServer(root);
            await body(server, url);
        }
        finally
        {
            ScrolllerPolicy.Apply(enabled, null);
            ScrolllerPolicy.Away = away;
            ScrolllerDiskCache.ResetForTests();
            ScrolllerDiskCache.RootProvider = oldRoot;
            ScrolllerDiskCache.FormatCheck = oldFormat;
            ScrolllerDiskCache.RemoteConsent = oldConsent;
            ScrolllerDiskCache.WarmInline = oldWarm;
            Directory.Delete(root, true);
        }
    }

    private sealed class Headers : WebViewWebRequestHeaders
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public override int Count => _values.Count;
        public override IEnumerable<string> Keys => _values.Keys;
        public override IEnumerable<string> Values => _values.Values;
        public override bool TrySet(string name, string value) { _values[name] = value; return true; }
        public override bool TryRemove(string name) => _values.Remove(name);
        public override bool ContainsKey(string key) => _values.ContainsKey(key);
        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
    }

    [Theory]
    [InlineData("https://foreign.example/", "https://cclabs.app/")]
    [InlineData("https://ccp.game/", "https://ccp.game/")]
    public void RequestsCarryHonestIdentity(string referer, string expected)
    {
        var headers = new Headers();
        headers.TrySet("Referer", referer);
        WebHost.StampScrolllerHeaders(new Uri("https://images.scrolller.com/a.jpg"), headers);
        Assert.Equal(ScrolllerSource.HonestUserAgent, headers["User-Agent"]);
        Assert.Equal(expected, headers["Referer"]);
        var foreign = new Headers();
        WebHost.StampScrolllerHeaders(new Uri("https://example.com/a.jpg"), foreign);
        Assert.Equal(0, foreign.Count);
    }

    /// <summary>WPF gates every WebView2 host except the in-app browser (BrowserService; WPF
    /// ScrolllerWebGateTests.Every_webview_host_attaches_the_gate; DeeperEditorWindow.xaml.cs:510 attaches it).
    /// The Deeper preview is gated; Home's browser is not.</summary>
    [Fact]
    public async Task PanicAndSwitchGateTheDeeperPreviewButLeaveTheHomeBrowser() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var enabled = ScrolllerPolicy.Enabled;
        var media = new Uri("https://images.scrolller.com/a.jpg");
        var editor = new DeeperEditorWindow();
        try
        {
            var gated = editor.BrowserPreview;
            var browser = new SettingsTabView().FindControl<WebHost>("BrowserWebHost")!;
            ScrolllerPolicy.Apply(false, null);
            Assert.False(gated.GateScrolllerNavigation(media));
            Assert.True(browser.GateScrolllerNavigation(media));
            ScrolllerPolicy.Apply(true, null);
            gated.Source = media;
            browser.Source = media;
            PanicSurfaces.All.Single(s => s.Id == "scrolller-web").Stop(null);
            Assert.Null(gated.Source);
            Assert.Equal(media, browser.Source);
            var headers = new Headers();
            browser.StampScrolllerHeadersIfGated(media, headers);
            Assert.Equal(0, headers.Count);
            gated.StampScrolllerHeadersIfGated(media, headers);
            Assert.Equal(ScrolllerSource.HonestUserAgent, headers["User-Agent"]);
        }
        finally
        {
            ScrolllerPolicy.Apply(enabled, null);
            editor.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task PanicClearsTheNavigatedHost() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        var enabled = ScrolllerPolicy.Enabled;
        ScrolllerPolicy.Apply(true, null);
        try
        {
            var host = new WebHost();
            host.Source = new Uri("https://images.scrolller.com/a.jpg");
            PanicSurfaces.All.Single(s => s.Id == "scrolller-web").Stop(null);
            Assert.Null(host.Source);
        }
        finally { ScrolllerPolicy.Apply(enabled, null); }
        return Task.CompletedTask;
    });
}
