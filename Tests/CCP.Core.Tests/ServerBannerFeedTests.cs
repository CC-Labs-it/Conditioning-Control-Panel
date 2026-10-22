using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The update-banner and marquee feeds (WPF MainWindow.Marquee.cs:740, :799-826) on a fake handler: no network.</summary>
public sealed class ServerBannerFeedTests
{
    private sealed class Feed(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Path = r.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static readonly Version Current = new(7, 1, 5);

    private static async Task<V2AuthService.ServerUpdateBanner?> Banner(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var feed = new Feed(body, status);
        var b = await new V2AuthService(() => null, feed).GetUpdateBannerAsync(Current);
        Assert.Equal("/config/update-banner", feed.Path);
        return b;
    }

    [Fact]
    public async Task UpdateBannerOnlyForANewerEnabledVersion()
    {
        var b = await Banner("{\"enabled\":true,\"version\":\"7.2.0\",\"url\":\"https://dl\"}");
        Assert.Equal("7.2.0", b!.Version);
        Assert.Equal("https://dl", b.Url);
        Assert.Null(await Banner("{\"enabled\":true,\"version\":\"7.1.5\"}"));     // same version
        Assert.Null(await Banner("{\"enabled\":true,\"version\":\"7.0.9\"}"));     // older
        Assert.Null(await Banner("{\"enabled\":false,\"version\":\"9.0.0\"}"));    // switched off
        Assert.Null(await Banner("{\"enabled\":true}"));                          // no version
        Assert.Null(await Banner("not json"));
        Assert.Null(await Banner("{\"enabled\":true,\"version\":\"9.0\"}", HttpStatusCode.InternalServerError));
    }

    [Fact]
    public async Task MarqueeFeedReturnsTheMessageOrNull()
    {
        var feed = new Feed("{\"message\":\"Spiral night\",\"board\":{}}");
        Assert.Equal("Spiral night", await new V2AuthService(() => null, feed).GetMarqueeMessageAsync());
        Assert.Equal("/config/marquee", feed.Path);
        Assert.Null(await new V2AuthService(() => null, new Feed("{\"message\":\"  \"}")).GetMarqueeMessageAsync());
        Assert.Null(await new V2AuthService(() => null, new Feed("[]")).GetMarqueeMessageAsync());
        Assert.Null(await new V2AuthService(() => null, new Feed("{}", HttpStatusCode.NotFound)).GetMarqueeMessageAsync());
    }
}
