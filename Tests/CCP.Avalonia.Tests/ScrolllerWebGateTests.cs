using System;
using System.IO;
using System.Collections.Generic;
using Avalonia.Controls;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
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
    public async Task CachedReplayIsFreeWhileAwayButSwitchOffWins()
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
            ScrolllerPolicy.Away = true;
            using (var writer = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta("picture")))
            {
                Assert.NotNull(writer);
                writer!.Stream.Write(new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0, 0, 0, 0 });
                writer.Commit();
            }
            // Simulate a fresh process with the production asynchronous cache warm-up:
            // away/spent must not turn a still-loading disk hit into a refusal.
            ScrolllerDiskCache.Flush();
            ScrolllerDiskCache.ResetForTests();
            ScrolllerDiskCache.WarmInline = false;
            var before = ScrolllerAllowance.UsedToday;
            Assert.NotNull(await ScrolllerNavigationGate.ResolveAsync(url, CancellationToken.None));
            using var server = new WebAssetServer(root);
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

    [Fact]
    public async Task TokenRouteRefusesForeignUrlsAndPanicInvalidatesIssuedUrls()
    {
        using var server = new WebAssetServer(Path.GetTempPath());
        using var http = new HttpClient();
        var issued = server.ScrolllerUrl(new Uri("https://images.scrolller.com/a.jpg"));
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            PanicSurfaces.All.Single(s => s.Id == "scrolller-web").Stop(null);
            return Task.CompletedTask;
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(issued)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.GetAsync(server.ScrolllerUrl(new Uri("https://example.com/a.jpg")))).StatusCode);
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
