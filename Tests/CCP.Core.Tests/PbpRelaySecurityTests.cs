using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class PbpRelaySecurityTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        }
    }

    [Theory]
    [InlineData("/v2/pbp/../user/me")]
    [InlineData("/v2/pbp/%2e%2e/user/me")]
    [InlineData("/v2/pbp/%252e%252e/user/me")]
    [InlineData("/v2/pbp/..\\user/me")]
    [InlineData("/v2/pbp/%2Fuser/me")]
    [InlineData("/v2/pbp/lobby?next=https://other.test")]
    [InlineData("/v2/pbp/lobby\n")]
    public async Task Relay_RejectsParserAmbiguityBeforeSending(string path)
    {
        var capture = new Capture();
        using var http = new HttpClient(capture);
        var call = PbpHostRules.ReadNet(new JObject { ["id"] = "n", ["path"] = path });
        var result = await PbpHostRules.SendAsync(http, "https://proxy.test", call, "player-token", "7");
        Assert.Equal((0, "forbidden_path"), result);
        Assert.Null(capture.Seen);
    }

    [Theory]
    [InlineData("/v2/pbp/watch/m_0123456789abcdef/upto/1", "GET", false)]
    [InlineData("/v2/pbp/watch/m_0123456789abcdef/chunk/0?lang=en", "GET", false)]
    [InlineData("/v2/pbp/watch/m_0123456789abcdef/ReAcTiOnS/22/", "GET", false)]
    [InlineData("/v2/pbp/watch/m_0123456789abcdef/upto/1", "POST", true)]
    [InlineData("/v2/pbp/watch/m_0123456789abcdef", "GET", true)]
    [InlineData("/v2/pbp/watch/m_0123456789abcdef/fx", "POST", true)]
    [InlineData("/v2/pbp/lobby?name=a%20b", "GET", true)]
    public async Task Relay_PublicTapeGetsNoToken_SeatAndMutationKeepIt(string path, string method, bool token)
    {
        var capture = new Capture();
        using var http = new HttpClient(capture);
        var call = PbpHostRules.ReadNet(new JObject { ["id"] = "n", ["path"] = path, ["method"] = method });
        Assert.Equal((200, "ok"), await PbpHostRules.SendAsync(http, "https://proxy.test", call, "player-token", "7"));
        Assert.Equal("https://proxy.test" + path, capture.Seen!.RequestUri!.OriginalString);
        Assert.Equal(token, capture.Seen.Headers.Contains("X-Auth-Token"));
        if (token) Assert.Equal("player-token", capture.Seen.Headers.GetValues("X-Auth-Token").Single());
        Assert.Equal("7", capture.Seen.Headers.GetValues("X-Client-Version").Single());
    }
}
