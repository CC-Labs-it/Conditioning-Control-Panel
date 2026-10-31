using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Theory]
    [InlineData(true, HttpStatusCode.OK, 1)]
    [InlineData(false, HttpStatusCode.OK, 0)]
    [InlineData(true, HttpStatusCode.Unauthorized, 0)]
    public void Rain_sync_receipt_forwards_only_authenticated_prize_snapshots(bool snapshot, HttpStatusCode status, int expected)
        => WithFreshInstall(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            wire.SyncStatus = status;
            wire.SyncReply = snapshot
                ? "{\"success\":true,\"prizes\":{\"revision\":7,\"grants\":[\"cos.rain.dress.rose-flare\"]}}"
                : "{\"success\":true}";
            var calls = 0;
            sync.PrizesReceived = (id, prizes) =>
            {
                calls++;
                Assert.Equal("u1", id);
                Assert.Equal(7, prizes.Revision);
                Assert.Equal(new[] { "cos.rain.dress.rose-flare" }, prizes.Grants);
            };
            sync.MarkLoaded(Array.Empty<string>());
            await sync.PushAsync("rain test");
            Assert.Equal(expected, calls);
        });
}
