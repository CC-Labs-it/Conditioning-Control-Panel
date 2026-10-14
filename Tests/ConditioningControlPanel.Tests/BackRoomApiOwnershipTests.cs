using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The relay's counter replies into the WPF head's OwnershipService. The relay itself is
/// covered in Tests/CCP.Core.Tests/BackRoomApiTests; this is the one case that needs the head.</summary>
public class BackRoomApiOwnershipTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public string Body = "{\"ok\":true}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
    }

    [Fact]
    public async Task Counter_Buy_UnlocksOwnership_AndAReplayedOlderReceiptDoesNotRollBack()
    {
        var ownership = new OwnershipService(() => "uid-1", null, a => a());
        var h = new FakeHandler();
        var api = new BackRoomApi(new HttpClient(h), () => ("uid-1", "tok-1"), null, ownership.ApplySnapshot);
        h.Body = "{\"ok\":true,\"sp\":5,\"prizes\":{\"revision\":5,\"grants\":[\"fx.flash.pendulum\"]}}";
        await api.RelayAsync("counter", "buy", "idem-dddddddddddddddd", null, TestContext.Current.CancellationToken);
        Assert.True(ownership.IsGranted("fx.flash.pendulum"));

        h.Body = "{\"ok\":true,\"sp\":5,\"prizes\":{\"revision\":3,\"grants\":[]}}";
        await api.RelayAsync("counter", "state", null, null, TestContext.Current.CancellationToken);
        Assert.Equal(5, ownership.Revision);
        Assert.True(ownership.IsGranted("fx.flash.pendulum"));
    }
}
