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
public sealed class SyncHealthTests
{
    [Fact]
    public async Task CountsRealFailuresButNotSkipsAndClearsOnSuccess()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var wire = new Wire();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, wire);
        var counts = new List<int>();
        sync.SyncHealthChanged += (_, count) => counts.Add(count);
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        try
        {
            var s = settings.Current;
            s.UnifiedId = "health-test";
            s.AuthToken = "fake";
            s.OfflineMode = false;
            s.LastConfirmedServerXp = 0;
            Assert.False(await sync.PushAsync("not loaded"));
            sync.MarkLoaded(null);
            wire.Status = (HttpStatusCode)429;
            Assert.False(await sync.PushAsync("clean rate limit"));
            Assert.Empty(counts);
            Assert.False(await sync.PushAsync("cooldown skip"));
            now += TimeSpan.FromHours(1);
            wire.Status = HttpStatusCode.InternalServerError;
            for (int i = 0; i < 2; i++)
            {
                Assert.False(await sync.PushAsync("failure"));
                if (i == 0) Assert.False(await sync.PushAsync("backoff skip"));
                now += TimeSpan.FromHours(1);
            }
            wire.Status = (HttpStatusCode)429;
            Assert.False(await sync.PushAsync("rate limit after failures"));
            Assert.Equal(new[] { 1, 2, 3 }, counts);
            Assert.False(await sync.PushAsync("cooldown skip"));
            now += TimeSpan.FromHours(1);
            Assert.False(await sync.PushAsync("another rate limit"));
            Assert.Equal(4, sync.ConsecutiveSyncFailures);
            now += TimeSpan.FromHours(1);
            wire.Status = HttpStatusCode.OK;
            Assert.True(await sync.PushAsync("recovered"));
            Assert.Equal(new[] { 1, 2, 3, 4, 0 }, counts);
            Assert.Equal(0, sync.ConsecutiveSyncFailures);
            now += TimeSpan.FromHours(1);
            wire.Status = (HttpStatusCode)429;
            Assert.False(await sync.PushAsync("clean rate limit after recovery"));
            Assert.Equal(new[] { 1, 2, 3, 4, 0 }, counts);
        }
        finally
        {
            sync.Reset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal HttpStatusCode Status = HttpStatusCode.InternalServerError;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("{}") });
    }
}
