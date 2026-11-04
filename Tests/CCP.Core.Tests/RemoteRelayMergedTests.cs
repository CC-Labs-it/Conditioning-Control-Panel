using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class RemoteRelayMergedTests
{
    private sealed class RelayHandler : HttpMessageHandler
    {
        public string? MergedPath;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var merged = path == MergedPath;
            var body = merged ? "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}"
                : path == "/v2/remote/start" ? "{\"code\":\"ABC123\"}" : "{}";
            return Task.FromResult(new HttpResponseMessage(merged ? HttpStatusCode.Conflict : HttpStatusCode.OK)
                { Content = new StringContent(body) });
        }
    }

    [Theory]
    [InlineData("/v2/remote/start")]
    [InlineData("/v2/remote/stop")]
    [InlineData("/v2/remote/poll")]
    [InlineData("/v2/remote/status")]
    [InlineData("/v2/remote/emote")]
    [InlineData("/v2/directory/opt-in")]
    public async Task Each_response_reaches_recovery_and_the_failed_request_stays_failed(string path)
    {
        var handler = new RelayHandler();
        var calls = 0;
        var commands = 0;
        var previous = RemoteRelay.MergedRecovery;
        RemoteRelay.MergedRecovery = async response =>
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                Assert.Contains("merged", await response.Content.ReadAsStringAsync());
                calls++;
            }
            return false; // even failed recovery must not make the command succeed
        };
        try
        {
            using var relay = new RemoteRelay(() => "token", () => "uid", "1", (_, _) => { commands++; return null; }, _ => { }, handler) { AutoPoll = false };
            if (path != "/v2/remote/start") Assert.NotNull(await relay.StartAsync("light"));
            handler.MergedPath = path;
            switch (path)
            {
                case "/v2/remote/start": Assert.Null(await relay.StartAsync("light")); Assert.False(relay.IsActive); break;
                case "/v2/remote/stop": await relay.StopAsync(); Assert.False(relay.IsActive); break;
                case "/v2/remote/poll": await relay.PollOnceAsync(); Assert.Equal(0, commands); break;
                case "/v2/remote/status": await relay.PushStatusNowAsync(); break;
                case "/v2/remote/emote": Assert.False((await relay.SendEmoteAsync("hi", "", "preset")).ok); break;
                case "/v2/directory/opt-in": Assert.False(await relay.OptInToDirectoryAsync(null, null)); Assert.False(relay.DirectoryOptedIn); break;
            }
            Assert.Equal(1, calls);
        }
        finally { RemoteRelay.MergedRecovery = previous; }
    }
}
