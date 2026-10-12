using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The LobbyWire seam: the head seeds the account door and proxy; unseeded nothing is sent.</summary>
[Collection(GoonOpenTablesCollection.Name)]
public class LobbyWireSeamTests
{
    private sealed class Counting : HttpMessageHandler
    {
        public int Calls;
        public string? LastUrl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"players\":[]}") });
        }
    }

    [Fact]
    public async Task Unseeded_wire_sends_nothing_and_reads_empty()
    {
        var (oldId, oldUrl) = (LobbyWire.DefaultIdentity, LobbyWire.DefaultBaseUrl);
        try
        {
            var h = new Counting();
            using var http = new HttpClient(h);
            LobbyWire.DefaultIdentity = () => ("u_me", "tok"); // signed in, but no proxy address seeded
            LobbyWire.DefaultBaseUrl = () => null;
            Assert.Same(PbpLobbyReply.Empty, await new PbpLobbyApi(http).FetchAsync(TestContext.Current.CancellationToken));
            Assert.Same(OpenTablesReply.Empty, await new GoonOpenTablesApi(http).FetchAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, h.Calls);

            LobbyWire.DefaultBaseUrl = () => "http://127.0.0.1:9";
            Assert.True((await new PbpLobbyApi(http).FetchAsync(TestContext.Current.CancellationToken)).Ok);
            Assert.Equal(1, h.Calls);
            Assert.Equal("http://127.0.0.1:9/v2/pbp/lobby?unified_id=u_me", h.LastUrl);
        }
        finally { (LobbyWire.DefaultIdentity, LobbyWire.DefaultBaseUrl) = (oldId, oldUrl); }
    }
}
