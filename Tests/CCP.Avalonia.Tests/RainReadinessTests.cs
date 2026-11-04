using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    private sealed class PrizeValidateWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"is_active\":true,\"tier\":1,\"unified_id\":\"u1\",\"prizes\":{\"revision\":7,\"grants\":[\"cos.rain.dress.rose-flare\"]}}") });
    }

    private sealed class RainWire : HttpMessageHandler
    {
        internal int Gets, Puts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get) Gets++; else if (request.Method == HttpMethod.Put) Puts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"equip\":{\"rain\":{\"rev\":1,\"items\":{},\"colors\":{\"hair\":\"#abcdef\"}},\"updatedAt\":\"2026-10-30T11:00:00Z\"}}") });
        }
    }

    [Fact]
    public void Readiness_pulls_and_adopts_then_local_change_pushes() => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (_, sync, _) = SignIn("u1", L40Profile("u1")); sync.PrizesReceived = RainLookSyncHead.ApplySyncPrizes;
        var s = CoreSettings.Current;
        var saved = (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc, s.RainPackHash);
        var client = RainLookSyncHead.Client;
        var available = RainLookSyncHead.Available;
        using var wire = new RainWire();
        try
        {
            s.RainLook = ""; s.RainLookPushOwed = false; s.RainLookAgreedUtc = null; s.RainPackHash = "old";
            RainLookSyncHead.Stop();
            var hooks = (RainLookSync.Hooks)typeof(RainLookSync).GetField("_h", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(client)!;
            RainLookSyncHead.Client = new RainLookSync(new HttpClient(wire), hooks);
            RainLookSyncHead.Available = () => true;
            Assert.True(await AccountSeed.LoadProfileAsync());
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, wire.Gets);
            Assert.Equal(RainLookCodec.Normalize("{\"rev\":1,\"items\":{},\"colors\":{\"hair\":\"#abcdef\"}}"), s.RainLook);
            Assert.Equal("", s.RainPackHash);
            Assert.NotNull(s.RainLookAgreedUtc);
            RainLookSyncHead.NoteLocalChange();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, wire.Puts);
            Assert.False(s.RainLookPushOwed);
        }
        finally
        {
            RainLookSyncHead.Stop(); RainLookSyncHead.Client = client; RainLookSyncHead.Available = available;
            (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc, s.RainPackHash) = saved;
        }
    }));

    private sealed class HeldWire : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<HttpResponseMessage> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Reply.Task;
    }

    [Fact]
    public void Account_switch_drops_held_and_late_snapshots() => WithFreshInstall(async () =>
    {
        SignIn("u1", null);
        PrizeOwnership.ApplySnapshot("u1", 3, new[] { "cos.rain.dress.rose-flare" });
        using var wire = new HeldWire();
        var api = new ConditioningControlPanel.Services.BackRoom.BackRoomApi(new HttpClient(wire), FriendsHead.Identity, null, PrizeOwnership.ApplySnapshot);
        var request = api.RelayAsync("counter", "state", null, null);
        CoreSettings.Current.UnifiedId = "u2"; CoreAccount.UnifiedUserId = "u2";
        CoreSettings.Current.UnifiedId = "u1"; CoreAccount.UnifiedUserId = "u1";
        Assert.False(PrizeOwnership.HasSnapshot("u1"));
        wire.Reply.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"prizes\":{\"revision\":4,\"grants\":[\"cos.rain.dress.rose-flare\"]}}") });
        await request;
        Assert.False(PrizeOwnership.HasSnapshot("u1"));
    });

    [Fact]
    public void Profile_parse_failure_does_not_suppress_prize_receipt() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        wire.SyncReply = "{\"success\":true,\"user\":{\"level\":\"bad\"},\"prizes\":{\"revision\":8,\"grants\":[\"cos.rain.dress.rose-flare\"]}}";
        PrizesBlock? received = null;
        sync.PrizesReceived = (_, block) => received = block;
        sync.MarkLoaded(Array.Empty<string>());
        Assert.True(await sync.PushAsync("prizes before profile"));
        Assert.NotNull(received);
        Assert.Equal(8, received.Revision);
        Assert.Equal(new[] { "cos.rain.dress.rose-flare" }, received.Grants);
    });

    [Fact]
    public void Provider_validate_delivers_ownership_before_rain_readiness() => WithFreshInstall(() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SignIn("u1", null);
        _secrets["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        using var wire = new PrizeValidateWire();
        Assert.True(AccountSeed.Seed(prefix => new ProviderSubscription(prefix, () => CoreSettings.Current, wire)));
        PrizeOwnership.ResetHeld();
        try
        {
            await AccountSeed.Patreon!.ValidateSubscriptionAsync(forceRefresh: true);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(PrizeOwnership.Held("cos.rain.dress.rose-flare"));
        }
        finally { PrizeOwnership.ResetHeld(); }
    }));
}
