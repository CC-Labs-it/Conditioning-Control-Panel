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
        internal string? Reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            OwnedAtRequest = PrizeOwnership.Held("cos.rain.dress.rose-flare");
            return Task.FromResult(Reply == null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply) });
        }
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK, 1)]
    [InlineData(false, HttpStatusCode.OK, 0)]
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
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_snapshot_waits_for_owned_snapshot_before_adopting_server_look(bool provider)
        => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            var saved = RainLookSyncHead.Client;
            var s = CoreSettings.Current;
            var look = (s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc);
            const string dress = "{\"rev\":1,\"items\":{\"dress\":\"rose-flare\"},\"colors\":{}}";
            using var rain = new RainReceiptWire { Reply = "{\"ok\":true,\"equip\":{\"rain\":" + dress + ",\"updatedAt\":\"2026-11-01T12:00:00Z\"}}" };
            try
            {
                PrizeOwnership.ResetHeld(); RainLookSyncHead.ForgetPulled();
                (s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc) = ("", "", false, null);
                RainLookSyncHead.Client = new RainLookSync(new HttpClient(rain), RainLookSyncHead.Hooks("https://proxy.test"));
                sync.MarkLoaded(Array.Empty<string>());
                wire.SyncReply = "{\"success\":true}";
                Assert.True(await sync.PushAsync("no snapshot"));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(0, rain.Calls);
                Assert.Equal("", s.RainLook);
                Assert.Null(s.RainLookAgreedUtc);

                if (provider)
                    ConditioningControlPanel.Services.ProviderSubscription.PrizesSink!("u1", "u1",
                        new ConditioningControlPanel.Models.PrizesBlock { Revision = 7, Grants = new() { "cos.rain.dress.rose-flare" } }, "validate");
                else
                {
                    wire.SyncReply = "{\"success\":true,\"prizes\":{\"revision\":7,\"grants\":[\"cos.rain.dress.rose-flare\"]}}";
                    sync.UtcNow = () => T0.AddMinutes(2);
                    Assert.True(await sync.PushAsync("owned snapshot"));
                }
                for (var i = 0; i < 200 && s.RainLookAgreedUtc == null; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                Assert.Equal(1, rain.Calls);
                Assert.True(rain.OwnedAtRequest);
                Assert.Equal(RainLookCodec.Normalize(dress), s.RainLook);
                Assert.NotNull(s.RainLookAgreedUtc);
            }
            finally
            {
                RainLookSyncHead.ForgetPulled(); RainLookSyncHead.Client = saved; PrizeOwnership.ResetHeld();
                (s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc) = look;
            }
        }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_snapshot_may_pull_when_an_earlier_snapshot_is_known(bool owned)
        => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            var saved = RainLookSyncHead.Client;
            using var rain = new RainReceiptWire();
            try
            {
                PrizeOwnership.ResetHeld(); RainLookSyncHead.ForgetPulled();
                PrizeOwnership.ApplySnapshot("u1", 7, owned ? new[] { "cos.rain.dress.rose-flare" } : Array.Empty<string>());
                RainLookSyncHead.Client = new RainLookSync(new HttpClient(rain), RainLookSyncHead.Hooks("https://proxy.test"));
                sync.MarkLoaded(Array.Empty<string>());
                wire.SyncReply = "{\"success\":true}";
                Assert.True(await sync.PushAsync("known snapshot"));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(1, rain.Calls);
                Assert.Equal(owned, rain.OwnedAtRequest);
            }
            finally { RainLookSyncHead.ForgetPulled(); RainLookSyncHead.Client = saved; PrizeOwnership.ResetHeld(); }
        }));

    [Fact]
    public void A_different_accounts_known_snapshot_does_not_enable_the_receipt()
        => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            var saved = RainLookSyncHead.Client;
            using var rain = new RainReceiptWire();
            try
            {
                PrizeOwnership.ResetHeld(); RainLookSyncHead.ForgetPulled();
                CoreAccount.UnifiedUserId = "other";
                PrizeOwnership.ApplySnapshot("other", 7, new[] { "cos.rain.dress.rose-flare" });
                RainLookSyncHead.Client = new RainLookSync(new HttpClient(rain), RainLookSyncHead.Hooks("https://proxy.test"));
                sync.MarkLoaded(Array.Empty<string>());
                wire.SyncReply = "{\"success\":true}";
                Assert.True(await sync.PushAsync("different account"));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(0, rain.Calls);
            }
            finally { RainLookSyncHead.ForgetPulled(); RainLookSyncHead.Client = saved; PrizeOwnership.ResetHeld(); }
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
