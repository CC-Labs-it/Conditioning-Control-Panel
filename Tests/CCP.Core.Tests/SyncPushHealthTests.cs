using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public class SyncPushHealthTests
{
    private sealed class Wire : HttpMessageHandler
    {
        public bool Succeed;
        public HttpStatusCode FailureStatus = HttpStatusCode.InternalServerError;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Succeed ? HttpStatusCode.OK : FailureStatus) { Content = new StringContent("{}") });
    }

    [Fact]
    public async Task FailuresAndSuccessReportHealthButSkipsDoNot()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var wire = new Wire();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        var events = new List<int>();
        sync.SyncHealthChanged += (_, count) => events.Add(count);
        try
        {
            settings.Current.UnifiedId = "health-test";
            settings.Current.AuthToken = "fake";
            settings.Current.OfflineMode = false;
            settings.Current.LastConfirmedServerXp = 0;
            sync.MarkLoaded(null);
            for (var i = 0; i < 3; i++)
            {
                Assert.False(await sync.PushAsync("failure"));
                Assert.False(await sync.PushAsync("backoff skip"));
                now += TimeSpan.FromMinutes(20);
            }
            Assert.Equal(new[] { 1, 2, 3 }, events);
            wire.Succeed = true;
            Assert.True(await sync.PushAsync("success"));
            Assert.False(await sync.PushAsync("cooldown skip"));
            Assert.Equal(new[] { 1, 2, 3, 0 }, events);
        }
        finally
        {
            sync.Reset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }
    [Fact]
    public async Task WatermarkRefusalsCountWithoutBackoffAndSuccessClearsHealth()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new Wire { Succeed = true });
        var events = new List<int>();
        sync.SyncHealthChanged += (_, count) => events.Add(count);
        try
        {
            var s = settings.Current;
            s.UnifiedId = "watermark-health-test";
            s.AuthToken = "fake";
            s.OfflineMode = false;
            s.PendingDescentMigrationChoice = null;
            s.LastConfirmedServerXpAccount = s.UnifiedId;
            s.LastConfirmedServerXpSeason = s.CurrentSeason;
            s.LastConfirmedServerXp = ProfileAdopt.TotalXp(s) + 1000;
            sync.MarkLoaded(null);
            for (var i = 0; i < 3; i++) Assert.False(await sync.PushAsync("watermark"));
            Assert.Equal(new[] { 1, 2, 3 }, events);
            s.LastConfirmedServerXp = 0;
            Assert.True(await sync.PushAsync("watermark cleared"));
            Assert.Equal(new[] { 1, 2, 3, 0 }, events);
        }
        finally
        {
            sync.Reset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    [Fact]
    public async Task RateLimitsCountOnlyWhileAnErrorIsOutstanding()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var wire = new Wire { FailureStatus = (HttpStatusCode)429 };
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        var events = new List<int>();
        sync.SyncHealthChanged += (_, count) => events.Add(count);
        try
        {
            settings.Current.UnifiedId = "rate-limit-health-test";
            settings.Current.AuthToken = "fake";
            settings.Current.OfflineMode = false;
            settings.Current.LastConfirmedServerXp = 0;
            sync.MarkLoaded(null);
            Assert.False(await sync.PushAsync("initial rate limit"));
            Assert.Empty(events);
            now += TimeSpan.FromMinutes(20);
            wire.FailureStatus = HttpStatusCode.InternalServerError;
            Assert.False(await sync.PushAsync("error"));
            now += TimeSpan.FromMinutes(20);
            wire.FailureStatus = (HttpStatusCode)429;
            Assert.False(await sync.PushAsync("rate limit"));
            Assert.False(await sync.PushAsync("cooldown skip"));
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.False(await sync.PushAsync("rate limit again"));
            Assert.Equal(new[] { 1, 2, 3 }, events);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            wire.Succeed = true;
            Assert.True(await sync.PushAsync("success"));
            Assert.Equal(new[] { 1, 2, 3, 0 }, events);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            wire.Succeed = false;
            Assert.False(await sync.PushAsync("rate limit after success"));
            Assert.Equal(new[] { 1, 2, 3, 0 }, events);
        }
        finally
        {
            sync.Reset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    [Fact]
    public async Task HandledAndUnhandledConflictsCountButCooldownSkipsDoNot()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldRecovery = V2AuthService.MergedRecovery;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var wire = new Wire { FailureStatus = HttpStatusCode.Conflict };
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        var events = new List<int>();
        sync.SyncHealthChanged += (_, count) => events.Add(count);
        try
        {
            settings.Current.UnifiedId = "conflict-health-test";
            settings.Current.AuthToken = "fake";
            settings.Current.OfflineMode = false;
            settings.Current.LastConfirmedServerXp = 0;
            sync.MarkLoaded(null);
            V2AuthService.MergedRecovery = (_, _) => Task.FromResult(true);
            Assert.False(await sync.PushAsync("handled"));
            Assert.Equal(new[] { 1 }, events);
            V2AuthService.MergedRecovery = (_, _) => Task.FromResult(false);
            wire.FailureStatus = (HttpStatusCode)429;
            Assert.False(await sync.PushAsync("rate limited after merge"));
            Assert.Equal(new[] { 1, 2 }, events);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            wire.FailureStatus = HttpStatusCode.Conflict;
            for (var i = 0; i < 2; i++)
            {
                Assert.False(await sync.PushAsync("conflict"));
                Assert.False(await sync.PushAsync("cooldown"));
                now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            }
            Assert.Equal(new[] { 1, 2, 3, 4 }, events);
            wire.FailureStatus = (HttpStatusCode)429;
            Assert.False(await sync.PushAsync("rate limited"));
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, events);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            wire.Succeed = true;
            Assert.True(await sync.PushAsync("success"));
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 0 }, events);
        }
        finally
        {
            sync.Reset();
            V2AuthService.MergedRecovery = oldRecovery;
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

}
