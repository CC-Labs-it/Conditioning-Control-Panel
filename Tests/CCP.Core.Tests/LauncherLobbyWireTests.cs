using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Core.Tests;

public sealed class LauncherLobbyWireTests
{
    private sealed class Wire : HttpMessageHandler
    {
        internal bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/v2/directory/list", request.RequestUri!.AbsolutePath);
            Assert.NotEmpty(request.Headers.GetValues("X-Caller-Unified-Id"));
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent("{\"entries\":[{\"unified_id\":\"r\",\"display_name\":\"Host\"}]}") });
        }
    }

    [Fact]
    public async Task DirectoryKeepsLastGoodRowsOnlyForTheSameAccount()
    {
        var wire = new Wire();
        using var http = new HttpClient(wire);
        var caller = "a";
        var api = new RemoteLobbyApi(http, () => (caller, "token"), () => "https://example.invalid");
        var good = await api.FetchAsync();
        Assert.True(good.Ok);
        Assert.Equal("Host", Assert.Single(good.Seats).Name);
        wire.Fail = true;
        var failed = await api.FetchAsync();
        Assert.False(failed.Ok);
        Assert.Single(failed.Seats);
        caller = "b";
        Assert.Empty((await api.FetchAsync()).Seats);
    }
}
