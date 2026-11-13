using System;
using System.Net;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Lobby;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public class RemoteClaimRecoveryTests
{
    private readonly Dictionary<string, string?> _secrets = new();

    public RemoteClaimRecoveryTests()
    {
        CoreSecrets.RetrieveProvider = name => _secrets.GetValueOrDefault(name);
        CoreSecrets.StoreProvider = (name, value) => _secrets[name] = value;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply(request));
    }

    [Fact]
    public async Task SeededClaimDistinguishesMergedFromAnOrdinaryConflictAndSwapsIdentity()
    {
        MergedAccountRecovery.ResetForTest();
        try
        {
            var settings = CoreSettings.Current;
            settings.UnifiedId = "u_old123456";
            settings.AuthToken = "old-token";
            CoreAccount.UnifiedUserId = settings.UnifiedId;
            MergedAccountRecovery.SettingsForTest = () => settings;
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            Assert.NotNull(V2AuthService.MergedRecovery);
            Assert.NotNull(MergedAccountRecovery.StopSync);
            Assert.NotNull(MergedAccountRecovery.StampQuests);
            Assert.NotNull(MergedAccountRecovery.Reauthenticate);
            Assert.NotNull(MergedAccountRecovery.ReloadProfile);
            Assert.NotNull(MergedAccountRecovery.Finished);
            var sync = AccountSeed.Sync = new SyncPush(() => null, () => false, new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
            sync.MarkLoaded(null);
            var stopped = false;
            var stamped = "";
            var finished = false;
            var stop = MergedAccountRecovery.StopSync!;
            MergedAccountRecovery.StopSync = () => { stopped = true; stop(); };
            MergedAccountRecovery.StampQuests = id => stamped = id;
            MergedAccountRecovery.Reauthenticate = () => Task.FromResult(false);
            MergedAccountRecovery.Finished = (_, ok) => finished = !ok;
            string body = "{}";
            var client = new RemoteDirectoryClaimClient(new HttpClient(new Handler(_ =>
                new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(body) })),
                () => ("u_old123456", "old-token"), "https://example.test");
            Assert.Equal(RemoteClaimStatus.Conflict, (await client.ClaimAsync("subject")).Status);
            Assert.False(stopped);
            Assert.True(sync.Loaded);
            Assert.Equal("u_old123456", settings.UnifiedId);
            body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123456\"}";
            Assert.Equal(RemoteClaimStatus.Merged, (await client.ClaimAsync("subject")).Status);
            await MergedAccountRecovery.LastSwap;
            Assert.True(stopped);
            Assert.False(sync.Loaded);
            Assert.Equal("u_canon123456", stamped);
            Assert.Equal("u_canon123456", settings.UnifiedId);
            Assert.Equal(settings.UnifiedId, CoreAccount.UnifiedUserId);
            Assert.Null(settings.AuthToken);
            Assert.True(finished);
        }
        finally { MergedAccountRecovery.ResetForTest(); }
    }

    [Fact]
    public async Task ReauthenticationUsesStoredProviderAndAdoptsFreshToken()
    {
        var tokens = JsonConvert.SerializeObject(new PatreonTokenData
            { AccessToken = "provider-token", RefreshToken = "refresh", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _secrets["patreon_auth"] = tokens;
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
        var calls = 0;
        AccountSeed.NewV2 = () => new V2AuthService(handler: new Handler(req =>
        {
            calls++;
            Assert.Equal("/v2/auth/patreon", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"success\":true,\"auth_token\":\"fresh-token\",\"user\":{\"unified_id\":\"u_canon123456\",\"display_name\":\"Name\"}}") };
        }));
        Assert.True(await AccountSeed.ReauthenticateAfterMergedSwapAsync());
        Assert.Equal(1, calls);
        Assert.Equal("u_canon123456", CoreAccount.UnifiedUserId);
        Assert.Equal("fresh-token", CoreSettings.Current.AuthToken);
    }
}
