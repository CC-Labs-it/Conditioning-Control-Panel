using System;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class DescentServiceTests : IDisposable
{
    private readonly Func<SettingsService?>? _old = CoreSettings.ServiceProvider;
    private readonly Action<Action>? _dispatch = CoreDispatch.PostProvider;
    private readonly SettingsService _settings = new();
    private readonly Func<string, string?>? _retrieve = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _store = CoreSecrets.StoreProvider;
    private string? _token;

    public DescentServiceTests()
    {
        CoreSecrets.RetrieveProvider = _ => _token;
        CoreSecrets.StoreProvider = (_, value) => _token = value;
        CoreSettings.ServiceProvider = () => _settings;
        CoreDispatch.PostProvider = null;
        _settings.Current.OfflineMode = false;
        _settings.Current.UnifiedId = "test-account";
        _settings.Current.AuthToken = "test-token";
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _retrieve;
        CoreSecrets.StoreProvider = _store;
        CoreSettings.ServiceProvider = _old;
        CoreDispatch.PostProvider = _dispatch;
    }

    private static JObject Block(int days) => JObject.Parse("{descent:{devotion_days:" + days + "}}");

    [Fact]
    public async Task ReadFailureKeepsBlock_WithdrawalClearsIt_ResetDisarmsRecurringReads()
    {
        JObject? reply = Block(7);
        var reader = new DescentService(_ => Task.FromResult(reply));
        int changed = 0;
        reader.BlockChanged += (_, _) => changed++;
        Assert.True(await reader.RefreshAsync("initial", force: true));
        Assert.Equal(7, reader.Current!.DevotionDays);
        Assert.True(reader.HasSeenBlock);
        reply = null;
        await reader.RefreshAsync("transport failure", force: true);
        Assert.Equal(7, reader.Current!.DevotionDays);
        Assert.Equal(1, changed);
        reply = new JObject();
        await reader.RefreshAsync("withdrawal", force: true);
        Assert.Null(reader.Current);
        Assert.True(reader.HasSeenBlock); // seen once in this session, WPF's recurring gate
        Assert.Equal(2, changed);
        reader.Reset();
        Assert.False(reader.HasSeenBlock);
        Assert.Null(reader.Current);
        Assert.Equal(3, changed);
    }

    [Fact]
    public async Task SignoutDiscardsInFlightAccount_ThenNewAccountCanReadImmediately()
    {
        var pending = new TaskCompletionSource<JObject?>();
        var reader = new DescentService(_ => pending.Task);
        var read = reader.RefreshAsync("old account");
        reader.Reset();
        pending.SetResult(Block(99));
        await read;
        Assert.Null(reader.Current);
        Assert.False(reader.HasSeenBlock);
        Assert.True(await reader.RefreshAsync("new account"));
        Assert.Equal(99, reader.Current!.DevotionDays);
    }

    [Fact]
    public async Task SyncLifecycleSignsIn_RefreshesSeenBlocks_AndResetsOnLogout()
    {
        var oldLoggedIn = CoreAccount.IsLoggedInProvider;
        CoreAccount.IsLoggedInProvider = () => true;
        var fetched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new DescentService(_ =>
        {
            return Task.FromResult<JObject?>(Block(4));
        });
        reader.BlockChanged += (_, _) => fetched.TrySetResult(true);
        using var handler = new ReplyHandler();
        var sync = new SyncPush(() => null, () => false, handler) { DescentReader = reader };
        try
        {
            sync.MarkLoaded(null);
            sync.StartHeartbeat();
            await fetched.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(reader.HasSeenBlock);
            reader.Reset();
            reader.SeedForTest(new DescentBlock { DevotionDays = 4 }); // seen, with a clear fetch floor
            fetched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            reader.BlockChanged += (_, _) => { reads++; refreshed.TrySetResult(true); };
            Assert.True(await sync.PushAsync("test"));
            await Task.WhenAll(fetched.Task, refreshed.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(reads >= 1); // a successful sync really requested a fresh profile read
            sync.Reset();
            Assert.False(reader.HasSeenBlock);
            Assert.Null(reader.Current);
            Assert.False(sync.HeartbeatRunning);
            Assert.True(reads >= 1);
        }
        finally { sync.Reset(); CoreAccount.IsLoggedInProvider = oldLoggedIn; }
    }

    private sealed class ReplyHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.StringContent("{}") });
    }

    [Theory]
    [InlineData(true, "test-account", "test-token")]
    [InlineData(false, null, "test-token")]
    [InlineData(false, "test-account", null)]
    public async Task OfflineOrMissingIdentityNeverFetches(bool offline, string? id, string? token)
    {
        var s = _settings.Current;
        s.OfflineMode = offline; s.UnifiedId = id; s.AuthToken = token;
        int calls = 0;
        var reader = new DescentService(_ => { calls++; return Task.FromResult<JObject?>(Block(1)); });
        Assert.False(await reader.RefreshAsync("not ready", force: true));
        Assert.Equal(0, calls);
        Assert.Null(reader.Current);
    }

    [Fact]
    public async Task SameCredentialsInsideFloorCoalesce_NotifySurfacesDoesNotFetch()
    {
        int calls = 0, events = 0;
        var reader = new DescentService(_ => { calls++; return Task.FromResult<JObject?>(Block(3)); });
        reader.BlockChanged += (_, _) => events++;
        await reader.RefreshAsync("first");
        Assert.False(await reader.RefreshAsync("duplicate"));
        reader.NotifySurfaces("migration gate moved");
        Assert.Equal(1, calls);
        Assert.Equal(2, events);
        Assert.Equal(3, reader.Current!.DevotionDays);
    }
}
