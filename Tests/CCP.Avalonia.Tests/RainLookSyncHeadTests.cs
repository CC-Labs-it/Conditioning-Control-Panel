using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RainLookSyncHeadTests : IDisposable
{
    private sealed class Fake : HttpMessageHandler
    {
        internal int Calls;
        internal HttpMethod? LastMethod;
        internal Func<Task<HttpResponseMessage>>? Reply;
        internal bool WasCancelled;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            LastMethod = r.Method;
            WasCancelled = ct.IsCancellationRequested;
            return Reply?.Invoke() ?? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private readonly AppSettings s = CoreSettings.Current;
    private readonly RainLookSync _previous = RainLookSyncHead.Client;
    private readonly (string? Id, bool Offline, Func<string, string?>? Get, Action<string, string?>? Set, Func<string, bool> Grants) _saved =
        (CoreSettings.Current.UnifiedId, CoreSettings.Current.OfflineMode, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted);
    private readonly (string Look, DateTime? Agreed, bool Owed, string Pack) _look =
        (CoreSettings.Current.RainLook, CoreSettings.Current.RainLookAgreedUtc, CoreSettings.Current.RainLookPushOwed, CoreSettings.Current.RainPackHash);
    private bool _resetOwnership;

    public RainLookSyncHeadTests()
    {
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "dummy" : null;
        CoreSecrets.StoreProvider = (_, _) => { };
        s.UnifiedId = "rain-test"; s.OfflineMode = false;
        RainLookSyncHead.Stop();
    }

    public void Dispose()
    {
        if (_resetOwnership) { PrizeOwnership.IsGranted = _saved.Grants; PrizeOwnership.ResetHeld(); }
        RainLookSyncHead.Stop(); RainLookSyncHead.Client = _previous;
        (s.UnifiedId, s.OfflineMode) = (_saved.Id, _saved.Offline);
        (s.RainLook, s.RainLookAgreedUtc, s.RainLookPushOwed, s.RainPackHash) = _look;
        CoreSecrets.RetrieveProvider = _saved.Get; CoreSecrets.StoreProvider = _saved.Set;
    }

    [Fact]
    public async Task Only_current_account_pulls_once_and_panic_stops_the_client()
    {
        using var http = new Fake();
        s.AuthToken = "dummy";
        RainLookSyncHead.Client = new RainLookSync(new HttpClient(http), new RainLookSync.Hooks
        {
            Identity = () => ("rain-test", "dummy"), Local = () => ("", false, null),
            Agreed = (_, _) => throw new Exception("404 must not agree"),
            Adopt = (_, _) => throw new Exception("404 must not adopt"),
            BaseUrl = "https://rain.test",
        });
        await RainLookSyncHead.PullAsync("other", default);
        using var stale = new CancellationTokenSource();
        await RainLookSyncHead.PullAsync("rain-test", stale.Token);
        await RainLookSyncHead.PullAsync("rain-test", new CancellationToken(canceled: true));
        Assert.Equal(0, http.Calls);
        await RainLookSyncHead.PullAsync("rain-test", default);
        await RainLookSyncHead.PullAsync("rain-test", default);
        Assert.Equal(1, http.Calls);
        Assert.Contains(PanicSurfaces.All, s => s.Id == "rain-look-sync");
        foreach (var surface in PanicSurfaces.All)
            if (surface.Id == "rain-look-sync") surface.Stop(null);
        await RainLookSyncHead.PullAsync("rain-test", default);
        Assert.Equal(2, http.Calls);
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            RainLookSyncHead.Stop();
            http.Calls = 0;
            var first = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            http.Reply = () => http.Calls == 1 ? first.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            RainLookSyncHead.AccountReady("rain-test");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal(1, http.Calls);
            RainLookSyncHead.Stop();
            RainLookSyncHead.AccountReady("rain-test");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var callsWhileUnwinding = http.Calls;
            first.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            Assert.Equal(2, callsWhileUnwinding);
            Assert.False(http.WasCancelled);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Matching_validate_without_prizes_or_grants_still_syncs(bool emptyBlock, bool pushOwed)
        => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            using var http = new Fake();
            _resetOwnership = true;
            var owed = pushOwed;
            RainLookSyncHead.Client = new RainLookSync(new HttpClient(http), new RainLookSync.Hooks
            {
                Identity = () => ("rain-test", "dummy"), Local = () => ("", owed, null),
                Agreed = (_, _) => owed = false, Adopt = (_, _) => owed = false,
                BaseUrl = "https://rain.test",
            });
            var prizes = emptyBlock ? new PrizesBlock() : null;
            async Task Drain() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            RainLookSyncHead.ApplyPrizes("rain-test", "other", prizes, "test");
            RainLookSyncHead.ApplyPrizes("other", "other", prizes, "test");
            RainLookSyncHead.ApplyPrizes(null, null, prizes, "test");
            RainLookSyncHead.ApplySyncPrizes("rain-test", null);
            await Drain();
            Assert.Equal(0, http.Calls);
            RainLookSyncHead.ApplyPrizes("rain-test", "rain-test", prizes, "test");
            await Drain();
            Assert.Equal(1, http.Calls);
            Assert.Equal(pushOwed ? HttpMethod.Put : HttpMethod.Get, http.LastMethod);
            Assert.Equal(pushOwed, owed); // A 404 must leave an owed push intact.
            if (!emptyBlock && !pushOwed)
            {
                PrizeOwnership.ResetHeld();
                PrizeOwnership.IsGranted = PrizeOwnership.Held;
                s.RainLook = ""; s.RainLookAgreedUtc = null; s.RainLookPushOwed = false;
                var hooks = (RainLookSync.Hooks)typeof(RainLookSync).GetField("_h", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_previous)!;
                RainLookSyncHead.Stop();
                RainLookSyncHead.Client = new RainLookSync(new HttpClient(http), hooks);
                const string dress = """{"rev":1,"items":{"dress":"rose-flare"},"colors":{}}""";
                http.Reply = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true,"equip":{"rain":  """ + dress + """, "updatedAt":"2026-10-30T11:00:00Z"}}""") });
                http.Calls = 0;
                RainLookSyncHead.ApplyPrizes("rain-test", "rain-test", null, "test");
                await Drain();
                Assert.Equal(1, http.Calls);
                Assert.Equal("", s.RainLook);
                Assert.Null(s.RainLookAgreedUtc);
                RainLookSyncHead.ApplySyncPrizes("rain-test", new PrizesBlock { Revision = 3, Grants = new() { "cos.rain.dress.rose-flare" } });
                await Drain();
                Assert.Equal(2, http.Calls);
                Assert.Equal(RainLookCodec.Normalize(dress), s.RainLook);
                Assert.Equal(new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc), s.RainLookAgreedUtc);
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_account_switch_invalidates_snapshot_and_late_reply(bool lateReply)
    {
        _resetOwnership = true;
        s.AuthToken = "dummy"; CoreAccount.UnifiedUserId = "rain-test";
        Assert.True(AccountSeed.Seed());
        PrizeOwnership.ApplySnapshot("rain-test", 3, new[] { "cos.rain.dress.rose-flare" });
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new Fake { Reply = () => reply.Task };
        var api = new ConditioningControlPanel.Services.BackRoom.BackRoomApi(new HttpClient(http), FriendsHead.Identity, null, PrizeOwnership.ApplySnapshot, "https://rain.test");
        var request = api.RelayAsync("counter", "state", null, null);
        s.UnifiedId = "other"; CoreAccount.UnifiedUserId = "other";
        s.UnifiedId = "rain-test"; CoreAccount.UnifiedUserId = "rain-test";
        var retained = PrizeOwnership.HasSnapshot("rain-test");
        PrizeOwnership.ResetHeld();
        reply.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"prizes\":{\"revision\":4,\"grants\":[\"cos.rain.dress.rose-flare\"]}}") });
        await request;
        Assert.False(lateReply ? PrizeOwnership.HasSnapshot("rain-test") : retained);
    }

    [Fact]
    public Task Queued_prizes_from_before_a_direct_switch_are_dropped()
        => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            _resetOwnership = true;
            s.AuthToken = "dummy"; CoreAccount.UnifiedUserId = "rain-test";
            Assert.True(AccountSeed.Seed());
            RainLookSyncHead.ApplyPrizes("rain-test", "rain-test", new PrizesBlock { Revision = 3, Grants = new() { "cos.rain.dress.rose-flare" } }, "test", queueReady: false);
            s.UnifiedId = "other"; CoreAccount.UnifiedUserId = "other";
            s.UnifiedId = "rain-test"; CoreAccount.UnifiedUserId = "rain-test";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.False(PrizeOwnership.HasSnapshot("rain-test"));
        });

    [Fact]
    public async Task Logout_forgets_ownership_before_the_same_account_returns()
    {
        _resetOwnership = true;
        s.AuthToken = "dummy";
        PrizeOwnership.ApplySnapshot("rain-test", 3, new[] { "cos.rain.dress.rose-flare" });
        Assert.True(PrizeOwnership.HasSnapshot("rain-test"));
        await AccountSeed.Logout(accountDeleted: true);
        s.UnifiedId = "rain-test"; s.AuthToken = "dummy";
        Assert.False(PrizeOwnership.HasSnapshot("rain-test"));
    }

    [Fact]
    public async Task Counter_reply_from_before_logout_is_not_applied_after_relogin()
    {
        _resetOwnership = true;
        s.AuthToken = "dummy";
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new Fake { Reply = () => reply.Task };
        var api = new ConditioningControlPanel.Services.BackRoom.BackRoomApi(new HttpClient(http), FriendsHead.Identity, null, PrizeOwnership.ApplySnapshot, "https://rain.test");
        var request = api.RelayAsync("counter", "state", null, null);
        await AccountSeed.Logout(accountDeleted: true);
        s.UnifiedId = "rain-test"; s.AuthToken = "dummy";
        reply.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"prizes\":{\"revision\":4,\"grants\":[\"cos.rain.dress.rose-flare\"]}}") });
        await request;
        Assert.False(PrizeOwnership.HasSnapshot("rain-test"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"rev\":1,\"items\":{},\"colors\":{\"hair\":\"#ffffff\"}}")]
    public void Default_server_look_settles_without_ownership_snapshot(string look)
    {
        _resetOwnership = true;
        PrizeOwnership.ResetHeld();
        s.RainLook = "old"; s.RainLookAgreedUtc = null; s.RainLookPushOwed = true;
        var hooks = (RainLookSync.Hooks)typeof(RainLookSync).GetField("_h", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_previous)!;
        var at = new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc);
        hooks.Adopt(look, at);
        Assert.Equal(RainLookCodec.Normalize(look), s.RainLook);
        Assert.Equal(at, s.RainLookAgreedUtc);
        Assert.False(s.RainLookPushOwed);
    }

    [Fact]
    public void Stop_cancels_a_source_captured_by_a_concurrent_reader_without_disposing_it()
    {
        // ApplyPrizes can read the source immediately before Stop swaps it, then read Token afterward.
        var field = typeof(RainLookSyncHead).GetField("_run",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var readerSource = Assert.IsType<CancellationTokenSource>(field.GetValue(null));
        RainLookSyncHead.Stop();
        Assert.True(readerSource.Token.IsCancellationRequested);
        var nextSource = Assert.IsType<CancellationTokenSource>(field.GetValue(null));
        Assert.NotSame(readerSource, nextSource);
        Assert.False(nextSource.Token.IsCancellationRequested);
    }
}
