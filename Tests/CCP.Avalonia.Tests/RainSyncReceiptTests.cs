using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Companion;
using System.Threading.Tasks;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    private sealed class RainReceiptWire : HttpMessageHandler
    {
        internal int Calls;
        internal bool HeldAtRequest;
        internal HttpMethod? Method;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Method = request.Method;
            HeldAtRequest = PrizeOwnership.Held("cos.rain.dress.rose-flare");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sync_receipt_queues_one_pull_after_any_snapshot(bool snapshot)
        => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            var s = CoreSettings.Current;
            var saved = (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc);
            var client = RainLookSyncHead.Client;
            using var rain = new RainReceiptWire();
            using var http = new HttpClient(rain);
            try
            {
                PrizeOwnership.ResetHeld(); RainLookSyncHead.Stop();
                s.RainLook = ""; s.RainLookPushOwed = false; s.RainLookAgreedUtc = null;
                RainLookSyncHead.Client = new RainLookSync(http, RainLookSyncHead.AppHooks);
                wire.SyncReply = snapshot ? "{\"success\":true,\"prizes\":{\"revision\":7,\"grants\":[\"cos.rain.dress.rose-flare\"]}}" : "{\"success\":true}";
                sync.PrizesReceived = RainLookSyncHead.ApplySyncPrizes;
                sync.MarkLoaded(Array.Empty<string>());
                Assert.True(await sync.PushAsync("receipt"));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(1, rain.Calls);
                Assert.Equal(HttpMethod.Get, rain.Method);
                Assert.Equal(snapshot, rain.HeldAtRequest);
                Assert.Equal(snapshot, PrizeOwnership.HasSnapshot("u1"));
                sync.UtcNow = () => T0.AddMinutes(2);
                Assert.True(await sync.PushAsync("receipt again"));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(1, rain.Calls);
            }
            finally
            {
                RainLookSyncHead.Stop(); RainLookSyncHead.Client = client; PrizeOwnership.ResetHeld();
                (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc) = saved;
            }
        }));

    [Theory]
    [InlineData(true, HttpStatusCode.OK, 1)]
    [InlineData(false, HttpStatusCode.OK, 1)]
    [InlineData(true, HttpStatusCode.Unauthorized, 0)]
    public void Rain_sync_receipt_forwards_authenticated_readiness_even_without_prizes(bool snapshot, HttpStatusCode status, int expected)
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
                if (snapshot)
                {
                    Assert.NotNull(prizes);
                    Assert.Equal(7, prizes.Revision);
                    Assert.Equal(new[] { "cos.rain.dress.rose-flare" }, prizes.Grants);
                }
                else Assert.Null(prizes);
            };
            sync.MarkLoaded(Array.Empty<string>());
            await sync.PushAsync("rain test");
            Assert.Equal(expected, calls);
        });

    [Theory]
    [InlineData("{\"revision\":null}", false)]
    [InlineData("{\"revision\":7,\"grants\":[]}", true)]
    public void Prize_failure_cannot_skip_the_descent_migration_ack(string prizes, bool callbackThrows)
        => WithFreshInstall(async () =>
        {
            var s = ConditioningControlPanel.CoreSettings.Current;
            var (wire, sync, _) = SignIn("u1", null);
            s.DescentCycleXpBonus = 1.0;
            s.DescentCycle = 0;
            s.PendingDescentMigrationChoice = "restore";
            wire.SyncReply = "{\"success\":true,\"prizes\":" + prizes
                + ",\"descent_migration\":{\"completed\":true,\"choice\":\"cycle\"}}";
            var calls = 0;
            sync.PrizesReceived = (_, _) => { calls++; if (callbackThrows) throw new InvalidOperationException("head failed"); };
            sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("ack survives prizes"));
            Assert.True(s.DescentMigrationCompleted);
            Assert.Equal("cycle", s.DescentMigrationChoice);
            Assert.Null(s.PendingDescentMigrationChoice);
            Assert.Equal(1, s.DescentCycle);
            Assert.Equal(ConditioningControlPanel.Services.Descent.DescentCycleXp.CycleXpBonus, s.DescentCycleXpBonus);
            Assert.Equal(callbackThrows ? 1 : 0, calls);
        });

    [Fact]
    public void Profile_parse_failure_cannot_skip_prizes_or_Rain_readiness()
        => WithFreshInstall(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            wire.SyncReply = """{"success":true,"user":{"level":"bad"},"prizes":{"revision":8,"grants":["cos.rain.dress.rose-flare"]}}""";
            var reply = Newtonsoft.Json.Linq.JObject.Parse(wire.SyncReply);
            Assert.Throws<Newtonsoft.Json.JsonReaderException>(() =>
                ConditioningControlPanel.Services.ProfileAdopt.ApplySyncResponse(
                    ConditioningControlPanel.CoreSettings.Current, reply, DateTime.UtcNow));
            ConditioningControlPanel.Models.PrizesBlock? delivered = null;
            string? account = null;
            sync.PrizesReceived = (id, prizes) => { account = id; delivered = prizes; };
            sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("prizes survive profile parse"));
            Assert.Equal("u1", account);
            Assert.NotNull(delivered);
            Assert.Equal(8, delivered.Revision);
            Assert.Equal(new[] { "cos.rain.dress.rose-flare" }, delivered.Grants);
        });
}
