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
/// The loopback relay under load (WebAssetServer's relay port): slow Scrolller fetches never starve a page's own files,
/// not even in a client that caps connections per origin the way a web engine does; a relay request with no free slot
/// gets a 503 at once; relay answers are readable cross-origin; closing the server cancels the fetches it started.
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
    private WebAssetServer MakeHanging()
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
        };
    }

    /// <summary>One client capped at 6 connections per origin, as a web engine's shared profile is (Chromium, libsoup).</summary>
    private static async Task<HttpClient> Signed(WebAssetServer s)
    {
        var http = new HttpClient(new SocketsHttpHandler { UseCookies = true, CookieContainer = new CookieContainer(), MaxConnectionsPerServer = 6 })
            { Timeout = TimeSpan.FromSeconds(20) };
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(s.Url("index.html"))).StatusCode);   // the token cookie
        return http;
    }

    private static string Relay(WebAssetServer s, int i) => s.RelayUrl($"https://images.scrolller.com/abc/clip-{i}.jpg")!;

    private static async Task Until(Func<bool> done, int ms = 10_000)
    {
        for (int t = 0; t < ms && !done(); t += 20) await Task.Delay(20);
        Assert.True(done());
    }

    [Fact]
    public async Task Slow_relay_fetches_never_starve_a_page_file_in_a_per_origin_capped_client()
    {
        using var s = MakeHanging();
        using var http = await Signed(s);
        // Six hanging relays fill one origin's six connections; on the page's own origin the page file then queued
        // behind them inside the client (as in the engine), so it never got an answer.
        var hanging = Enumerable.Range(0, 6).Select(i => http.GetAsync(Relay(s, i))).ToList();
        await Until(() => Volatile.Read(ref _started) >= 6);
        using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var page = await http.GetAsync(s.Url("index.html"), quick.Token);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.NotEqual(s.Port, new Uri(Relay(s, 0)).Port);
    }

    [Fact]
    public async Task A_relay_request_with_no_free_slot_gets_a_503_at_once()
    {
        using var s = MakeHanging();
        using var http = await Signed(s);
        var hanging = Enumerable.Range(0, 6).Select(i => http.GetAsync(Relay(s, i))).ToList();
        await Until(() => Volatile.Read(ref _started) >= 6);
        using var other = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };   // its own pool: the server's answer, not a client queue
        var late = await other.GetAsync(Relay(s, 99));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, late.StatusCode);
        Assert.NotNull(late.Headers.RetryAfter);
        Assert.Contains("*", late.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Equal(6, Volatile.Read(ref _started));
    }

    [Fact]
    public async Task Relay_answers_are_readable_cross_origin_and_need_the_token()
    {
        using var s = MakeHanging();
        using var http = new HttpClient();
        var refused = await http.GetAsync(s.RelayOrigin + ScrolllerRelay.ToRelayPath("https://images.scrolller.com/a/b.jpg"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);   // no token in the path
        Assert.Contains("*", refused.Headers.GetValues("Access-Control-Allow-Origin"));
        var preflight = await http.SendAsync(new HttpRequestMessage(HttpMethod.Options, Relay(s, 1)));
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Contains("Range", string.Join(",", preflight.Headers.GetValues("Access-Control-Allow-Headers")));
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
