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
/// The loopback relay under load (WebAssetServer /scrolller-media/): slow Scrolller fetches never stall the accept loop
/// (every other page file still answers), a request with no free slot gets a 503 instead of a hang, and closing the
/// server cancels the fetches it started.
/// </summary>
[Collection(ScrolllerRelayCollection.Name)]   // ScrolllerPolicy is process-wide
public sealed class ScrolllerRelayLoadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-relay-load-").FullName;
    private int _started, _cancelled;

    public void Dispose()
    {
        ScrolllerPolicy.Apply(true, null);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>A server whose every relay fetch hangs until it is cancelled.</summary>
    private WebAssetServer MakeHanging(TimeSpan? slotWait = null)
    {
        var web = Path.Combine(_dir, "web");
        Directory.CreateDirectory(web);
        File.WriteAllText(Path.Combine(web, "index.html"), "<p>hi</p>");
        return new WebAssetServer(web)
        {
            ScrolllerCached = _ => null,
            ScrolllerMayFetchFresh = _ => true,
            ScrolllerFetch = async (_, ct) =>
            {
                Interlocked.Increment(ref _started);
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref _cancelled); throw; }
                return null;
            },
            SlotWait = slotWait ?? TimeSpan.FromSeconds(30),
        };
    }

    private static async Task<HttpClient> Signed(WebAssetServer s)
    {
        var http = new HttpClient(new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() })
            { Timeout = TimeSpan.FromSeconds(20) };
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(s.Url("index.html"))).StatusCode);   // the token cookie
        return http;
    }

    private static string Relay(WebAssetServer s, int i) =>
        $"http://127.0.0.1:{s.Port}{ScrolllerRelay.ToRelayPath($"https://images.scrolller.com/abc/clip-{i}.jpg")}";

    private static async Task Until(Func<bool> done, int ms = 10_000)
    {
        for (int t = 0; t < ms && !done(); t += 20) await Task.Delay(20);
        Assert.True(done());
    }

    [Fact]
    public async Task Slow_relay_fetches_never_block_a_page_file()
    {
        using var s = MakeHanging();
        using var http = await Signed(s);
        // Every relay slot busy, plus one more queued behind them: the old loop waited for a slot before accepting the
        // next request, so the page file below never got an answer.
        var hanging = Enumerable.Range(0, 7).Select(i => http.GetAsync(Relay(s, i))).ToList();
        await Until(() => Volatile.Read(ref _started) >= 6);
        using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var page = await http.GetAsync(s.Url("index.html"), quick.Token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(6, Volatile.Read(ref _started));   // the seventh waits for a slot, it never fetched
    }

    [Fact]
    public async Task A_relay_request_with_no_free_slot_gets_a_503_not_a_hang()
    {
        using var s = MakeHanging(TimeSpan.FromMilliseconds(300));
        using var http = await Signed(s);
        var hanging = Enumerable.Range(0, 6).Select(i => http.GetAsync(Relay(s, i))).ToList();
        await Until(() => Volatile.Read(ref _started) >= 6);
        var late = await http.GetAsync(Relay(s, 99));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, late.StatusCode);
        Assert.NotNull(late.Headers.RetryAfter);
        Assert.Equal(6, Volatile.Read(ref _started));
    }

    [Fact]
    public async Task A_head_on_an_uncached_url_is_a_404_and_never_fetches()
    {
        using var s = MakeHanging();
        using var http = await Signed(s);
        var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, Relay(s, 1)));
        Assert.Equal(HttpStatusCode.NotFound, head.StatusCode);
        Assert.Equal(0, Volatile.Read(ref _started));
    }

    [Fact]
    public async Task A_head_on_a_cached_url_answers_headers_only()
    {
        var file = Path.Combine(_dir, "c.jpg");
        File.WriteAllBytes(file, new byte[10]);
        var web = Path.Combine(_dir, "web");
        Directory.CreateDirectory(web);
        File.WriteAllText(Path.Combine(web, "index.html"), "<p>hi</p>");
        using var s = new WebAssetServer(web)
        {
            ScrolllerCached = _ => file,
            ScrolllerMayFetchFresh = _ => true,
            ScrolllerFetch = (_, _) => { Interlocked.Increment(ref _started); return Task.FromResult<string?>(null); },
        };
        using var http = await Signed(s);
        var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, Relay(s, 1)));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(10, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, Volatile.Read(ref _started));
    }

    [Fact]
    public async Task Closing_the_server_cancels_its_relay_fetches()
    {
        var s = MakeHanging();
        using var http = await Signed(s);
        var hanging = Enumerable.Range(0, 3).Select(i => http.GetAsync(Relay(s, i))).ToList();
        await Until(() => Volatile.Read(ref _started) >= 3);
        s.Dispose();
        await Until(() => Volatile.Read(ref _cancelled) >= 3);
    }
}
