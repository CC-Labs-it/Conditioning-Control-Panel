using System;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Net.Http;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class DescentServiceTests
{
    private static JObject Profile(int xp) => DescentReader.ParseWire(
        "{\"level\":4,\"xp\":120,\"current_season\":\"s1\",\"curve_epoch\":1," +
        "\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":4000,\"today_xp\":" + xp + ",\"fill_lip_pct\":120}}}")!;

    private static async Task Run(Func<Task> body)
    {
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.OfflineMode, CoreDispatch.PostProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        string? token = "test-token";
        try
        {
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? token : null;
            CoreSecrets.StoreProvider = (name, value) => { if (name == CoreSecrets.AuthToken) token = value; };
            (s.UnifiedId, s.OfflineMode) = ("vat-test", false);
            CoreDispatch.PostProvider = null;
            await body();
        }
        finally { (s.UnifiedId, s.OfflineMode, CoreDispatch.PostProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = saved; }
    }

    [Fact]
    public Task OfflineAndMissingCredentialsNeverFetchEvenWhenForced() => Run(async () =>
    {
        int calls = 0;
        var service = new DescentService(_ => { calls++; return Task.FromResult<JObject?>(Profile(1000)); });
        CoreSettings.Current.OfflineMode = true;
        Assert.False(await service.RefreshAsync("offline", force: true));
        CoreSettings.Current.OfflineMode = false;
        CoreSettings.Current.AuthToken = null;
        Assert.False(await service.RefreshAsync("signed out", force: true));
        Assert.Equal(0, calls);
        Assert.Null(service.Current);
        Assert.False(service.HasSeenBlock);
    });

    [Fact]
    public Task AcceptedReadsWithdrawTheBlockWhileFailedReadsKeepTheLastTruth() => Run(async () =>
    {
        JObject? reply = Profile(1000);
        var service = new DescentService(_ => Task.FromResult(reply));
        int changed = 0;
        service.BlockChanged += (_, _) => changed++;
        Assert.True(await service.RefreshAsync("open", force: true));
        var first = service.Current;
        Assert.Equal(1000, first!.Vat!.TodayXp);
        Assert.True(service.HasSeenBlock);
        Assert.False(await service.RefreshAsync("coalesced"));
        reply = null; // transport failure does not mean that the server withdrew the key
        Assert.True(await service.RefreshAsync("failure", force: true));
        Assert.Same(first, service.Current);
        Assert.Equal(1, changed);
        reply = new JObject();
        Assert.True(await service.RefreshAsync("withdraw", force: true));
        Assert.Null(service.Current);
        Assert.True(service.HasSeenBlock); // recurring callers remember this account until logout
        Assert.Equal(2, changed);
        service.Reset();
        Assert.False(service.HasSeenBlock);
        Assert.Equal(3, changed);
    });

    [Fact]
    public Task LogoutDiscardsAnOldAccountsInFlightReading() => Run(async () =>
    {
        var reply = new TaskCompletionSource<JObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DescentService(_ => reply.Task);
        int offers = 0;
        service.ProgressionReceived = (_, _, _, _) => offers++;
        var pending = service.RefreshAsync("old account", force: true);
        service.Reset();
        CoreSettings.Current.UnifiedId = "different-account";
        reply.SetResult(Profile(2000));
        Assert.True(await pending);
        Assert.Null(service.Current);
        Assert.False(service.HasSeenBlock);
        Assert.Equal(0, offers);
    });

    [Fact]
    public Task ProgressionOffersPreserveRawSeasonAndCurveAndObserverFaultsDoNotLoseTheBlock() => Run(async () =>
    {
        var service = new DescentService(_ => Task.FromResult<JObject?>(Profile(800)));
        (int, double, string?, int?)? offer = null;
        service.ProgressionReceived = (level, xp, season, epoch) =>
        {
            offer = (level, xp, season, epoch);
            throw new InvalidOperationException("head unavailable");
        };
        service.BlockChanged += (_, _) => throw new InvalidOperationException("observer unavailable");
        Assert.True(await service.RefreshAsync("offer", force: true));
        Assert.Equal((4, 120d, "s1", (int?)1), offer!.Value);
        Assert.Equal(800, service.Current!.Vat!.TodayXp);
    });

    [Fact]
    public Task SyncSignInAndAcceptedPushRefreshTheReaderAndLogoutClearsIt() => Run(async () =>
    {
        var savedAccount = CoreAccount.IsLoggedInProvider;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        var clock = new SteppedClock();
        var reader = new DescentService(_ => Task.FromResult<JObject?>(Profile(1000)), clock);
        reader.BlockChanged += (_, _) =>
        {
            if (reader.Current == null) return;
            if (++reads == 1) first.TrySetResult(); else second.TrySetResult();
        };
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new AcceptedSync()) { DescentReader = reader };
        try
        {
            CoreAccount.IsLoggedInProvider = () => false; // heartbeat issues no request in this phase
            sync.StartHeartbeat();
            await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
            sync.StopHeartbeat();
            Assert.True(reader.HasSeenBlock);
            // Step past the ten-second read floor instead of waiting it out (P08).
            clock.Now += TimeSpan.FromSeconds(10.1);
            CoreAccount.IsLoggedInProvider = () => true;
            sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("accepted test push"));
            await second.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, reads);
            sync.Reset();
            Assert.Null(reader.Current);
            Assert.False(reader.HasSeenBlock);
        }
        finally { sync.Reset(); CoreAccount.IsLoggedInProvider = savedAccount; }
    });

    [Fact]
    public Task AcceptedSyncRetainsLiveOfferUntilAckOrLogoutAndNotifiesSurfaces() => Run(async () =>
    {
        var account = CoreAccount.IsLoggedInProvider;
        var reader = new DescentService();
        int changes = 0; reader.BlockChanged += (_, _) => changes++;
        var sync = new SyncPush(() => Array.Empty<string>(), () => false,
            new AcceptedSync("{\"descent_migration\":{\"required\":true}}")) { DescentReader = reader };
        try
        {
            CoreAccount.IsLoggedInProvider = () => true; sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("live migration offer"));
            Assert.True(reader.HasMigrationOffer); Assert.Equal(1, changes);
            reader.ObserveMigrationOffer(new JObject());
            Assert.True(reader.HasMigrationOffer); Assert.Equal(1, changes);
            reader.ObserveMigrationOffer(JObject.Parse("{\"descent_migration\":{\"completed\":true}}"));
            Assert.False(reader.HasMigrationOffer); Assert.Equal(2, changes);
            reader.ObserveMigrationOffer(JObject.Parse("{\"descent_migration\":{\"required\":true}}"));
            Assert.True(reader.HasMigrationOffer);
            sync.Reset(); Assert.False(reader.HasMigrationOffer);
        }
        finally { sync.Reset(); CoreAccount.IsLoggedInProvider = account; }
    });

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AcceptedSync(string body = "{}") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
