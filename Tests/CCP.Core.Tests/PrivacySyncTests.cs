using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class PrivacySyncTests
{
    [Fact]
    public async Task PurchaseRetryWaitsOutCooldownOnceBeforeCreditingTheServer()
    {
        var previous = CoreSettings.ServiceProvider;
        var login = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var handler = new FakeSync();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        try
        {
            var s = settings.Current;
            s.UnifiedId = "purchase-test"; s.AuthToken = "fake"; s.OfflineMode = false;
            s.LastConfirmedServerXp = 0;
            var now = DateTime.UtcNow;
            sync.UtcNow = () => now;
            int waits = 0;
            sync.PurchaseRetryDelay = delay => { waits++; now += delay; return Task.CompletedTask; };
            sync.MarkLoaded(null);
            Assert.True(await sync.PushAsync("initial"));
            Assert.True(await sync.SyncBeforePurchaseRetryAsync());
            Assert.Equal(1, waits);
            Assert.Equal(2, handler.Bodies.Count);
        }
        finally { sync.Reset(); CoreSettings.ServiceProvider = previous; CoreAccount.IsLoggedInProvider = login; }
    }

    [Fact]
    public void OnlyChosenFieldsBelongToTheAccountAndUnrelatedDefaultsStayUnknown()
    {
        var s = new AppSettings { UnifiedId = "first", PublicShareRealAvatar = false, GoonShareAvatar = true };
        Assert.Equal(SyncBody.Field.None, SyncPush.ConsentFields(s));
        Assert.True(SyncPush.RememberPrivacyChange(s, SyncBody.Field.PublicShareAvatar));
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null, consent: SyncPush.ConsentFields(s))));
        Assert.Equal(JTokenType.Boolean, body["public_share_avatar"]!.Type);
        Assert.False(body.Value<bool>("public_share_avatar"));
        Assert.Null(body["goon_share_avatar"]);
        Assert.Null(body["allow_discord_dm"]);
        Assert.Null(body["show_online_status"]);
        Assert.Null(body["achievements"]);
        Assert.True(SyncPush.RememberPrivacyChange(s, SyncBody.Field.GoonShareAvatar));
        Assert.Equal(SyncBody.Field.PublicShareAvatar | SyncBody.Field.GoonShareAvatar, SyncPush.ConsentFields(s));
        s.UnifiedId = "second";
        Assert.Equal(SyncBody.Field.None, SyncPush.ConsentFields(s));
        Assert.True(SyncPush.RememberPrivacyChange(s, SyncBody.Field.AllowDiscordDm));
        Assert.Equal(SyncBody.Field.AllowDiscordDm, SyncPush.ConsentFields(s));
        Assert.Throws<ArgumentOutOfRangeException>(() => SyncPush.RememberPrivacyChange(s, SyncBody.Field.Xp));
        s.UnifiedId = null;
        Assert.False(SyncPush.RememberPrivacyChange(s, SyncBody.Field.PublicShareAvatar));
        Assert.Equal(SyncBody.Field.None, SyncPush.ConsentFields(s));
    }

    [Fact]
    public async Task OfflineRevokeSurvivesRestartAndCooldownCannotClearTheUnsentDebt()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var handler = new FakeSync();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        try
        {
            var s = settings.Current;
            s.UnifiedId = "privacy-test";
            s.AuthToken = "fake";
            s.LastConfirmedServerXp = 0;
            s.OfflineMode = true;
            s.PublicShareRealAvatar = false;
            Assert.False(await sync.PushPrivacyAsync(SyncBody.Field.PublicShareAvatar));
            Assert.Empty(handler.Bodies);
            settings.SaveImmediate();
            settings = new SettingsService();
            s = settings.Current;
            Assert.Equal(SyncBody.Field.PublicShareAvatar, SyncPush.ConsentFields(s));
            s.OfflineMode = false;
            var now = DateTime.UtcNow;
            sync.UtcNow = () => now;
            sync.MarkLoaded(null);
            Assert.True(await sync.PushAsync("after load"));
            Assert.Equal(JTokenType.Boolean, handler.Bodies[0]["public_share_avatar"]!.Type);
            Assert.False(handler.Bodies[0].Value<bool>("public_share_avatar"));
            Assert.Null(handler.Bodies[0]["goon_share_avatar"]);
            s.GoonShareDiscordDm = false;
            Assert.False(await sync.PushPrivacyAsync(SyncBody.Field.GoonShareDm));
            Assert.Single(handler.Bodies);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("after cooldown"));
            Assert.Equal(JTokenType.Boolean, handler.Bodies[1]["goon_share_dm"]!.Type);
            Assert.False(handler.Bodies[1].Value<bool>("goon_share_dm"));
            Assert.Null(handler.Bodies[1]["share_profile_picture"]);
        }
        finally
        {
            sync.Reset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    [Fact]
    public async Task ARevokeWhileAGrantIsInFlightStaysOwedUntilTheRevokeIsSent()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var handler = new FakeSync { Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        try
        {
            var s = settings.Current;
            s.UnifiedId = "inflight-test";
            s.AuthToken = "fake";
            s.LastConfirmedServerXp = 0;
            s.OfflineMode = false;
            s.PublicShareRealAvatar = true;
            var now = DateTime.UtcNow;
            sync.UtcNow = () => now;
            sync.MarkLoaded(null);
            var grant = sync.PushPrivacyAsync(SyncBody.Field.PublicShareAvatar);
            await handler.Received.Task;
            s.PublicShareRealAvatar = false;
            SyncPush.RememberPrivacyChange(s, SyncBody.Field.PublicShareAvatar);
            handler.Hold.SetResult(true);
            Assert.True(await grant);
            Assert.True(handler.Bodies[0].Value<bool>("public_share_avatar"));
            handler.Hold = null;
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("revoke"));
            Assert.Equal(JTokenType.Boolean, handler.Bodies[1]["public_share_avatar"]!.Type);
            Assert.False(handler.Bodies[1].Value<bool>("public_share_avatar"));
        }
        finally
        {
            handler.Hold?.TrySetResult(true);
            sync.Reset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    private sealed class FakeSync : HttpMessageHandler
    {
        internal readonly List<JObject> Bodies = new();
        internal TaskCompletionSource<bool>? Hold;
        internal readonly TaskCompletionSource<bool> Received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/v2/user/sync", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.Contains("X-CCP-Signature"));
            Bodies.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
            Received.TrySetResult(true);
            if (Hold != null) await Hold.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
