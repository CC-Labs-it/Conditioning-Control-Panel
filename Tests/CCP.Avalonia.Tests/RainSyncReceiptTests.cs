using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Companion.Rain;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    private sealed class RainReceiptWire : HttpMessageHandler
    {
        internal int Calls;
        internal bool OwnedAtRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            OwnedAtRequest = PrizeOwnership.Held("cos.rain.dress.rose-flare");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK, 1)]
    [InlineData(false, HttpStatusCode.OK, 1)]
    [InlineData(true, HttpStatusCode.Unauthorized, 0)]
    public void Sync_receipt_queues_readiness_after_ownership(bool snapshot, HttpStatusCode status, int expected)
        => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            var saved = RainLookSyncHead.Client;
            var s = CoreSettings.Current;
            var look = (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc);
            using var rain = new RainReceiptWire();
            try
            {
                PrizeOwnership.ResetHeld(); RainLookSyncHead.ForgetPulled();
                (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc) = ("", false, null);
                RainLookSyncHead.Client = new RainLookSync(new HttpClient(rain), RainLookSyncHead.Hooks("https://proxy.test"));
                wire.SyncStatus = status;
                wire.SyncReply = snapshot ? "{\"success\":true,\"prizes\":{\"revision\":7,\"grants\":[\"cos.rain.dress.rose-flare\"]}}" : "{\"success\":true}";
                sync.MarkLoaded(Array.Empty<string>());
                await sync.PushAsync("rain receipt");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(expected, rain.Calls);
                Assert.Equal(expected == 1 && snapshot, rain.OwnedAtRequest);
            }
            finally
            {
                RainLookSyncHead.ForgetPulled(); RainLookSyncHead.Client = saved; PrizeOwnership.ResetHeld();
                (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc) = look;
            }
        }));

    [Theory]
    [InlineData("{\"revision\":null}", false)]
    [InlineData("{\"revision\":7,\"grants\":[]}", true)]
    public void Bad_prizes_or_a_throwing_head_do_not_skip_migration_ack(string prizes, bool headThrows)
        => WithFreshInstall(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            var s = CoreSettings.Current;
            s.PendingDescentMigrationChoice = "restore";
            wire.SyncReply = "{\"success\":true,\"prizes\":" + prizes + ",\"descent_migration\":{\"completed\":true,\"choice\":\"cycle\"}}";
            int calls = 0;
            sync.ReceiptReceived = _ => { calls++; if (headThrows) throw new InvalidOperationException(); };
            sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("receipt failure"));
            Assert.True(s.DescentMigrationCompleted);
            Assert.Equal("cycle", s.DescentMigrationChoice);
            Assert.Null(s.PendingDescentMigrationChoice);
            Assert.Equal(headThrows ? 1 : 0, calls);
        });
    [Fact]
    public void Profile_parse_failure_cannot_skip_the_receipt()
        => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            wire.SyncReply = "{\"success\":true,\"user\":{\"level\":\"bad\"},\"prizes\":{\"revision\":8,\"grants\":[\"cos.rain.dress.rose-flare\"]}}";
            var calls = 0;
            sync.ReceiptReceived = id => { Assert.Equal("u1", id); calls++; };
            sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("profile parse failure"));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, calls);
            Assert.True(PrizeOwnership.Held("cos.rain.dress.rose-flare"));
        }));

}
