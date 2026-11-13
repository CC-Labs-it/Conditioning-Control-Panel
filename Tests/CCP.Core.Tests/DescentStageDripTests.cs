using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF DescentMigrationService.TickStageDrip (:390-440), called after each successful sync
/// (ProfileSyncService.cs:1915). Restack b172, the missing slice of #4045.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class DescentStageDripTests
{
    [Fact]
    public void StageQueueWaitsForAckAndReleasesOncePerLocalDay()
    {
        var s = new AppSettings { DescentPendingStageCeremonies = new List<int> { 1, 2 } };
        var today = new DateTime(2026, 10, 20, 23, 59, 0);
        Assert.Null(DescentMigration.ReleaseStageCeremony(s, today)); // not before the server acknowledged
        s.DescentMigrationCompleted = true;
        Assert.Equal(1, DescentMigration.ReleaseStageCeremony(s, today));
        Assert.Equal("2026-10-20", s.DescentLastStageDripDate);
        Assert.Null(DescentMigration.ReleaseStageCeremony(s, today)); // once per local day
        Assert.Equal(new[] { 2 }, s.DescentPendingStageCeremonies);
        Assert.Equal(2, DescentMigration.ReleaseStageCeremony(s, today.AddMinutes(1)));
        Assert.Null(DescentMigration.ReleaseStageCeremony(s, today.AddDays(1))); // an empty queue stays quiet
    }

    [Fact]
    public async Task ASuccessfulSyncReleasesOneStageAndAnUnreadableOneSpendsNone()
    {
        var old = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreDispatch.PostProvider);
        var settings = new SettingsService();
        var handler = new Reply();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreDispatch.PostProvider = null;
            var s = settings.Current;
            s.UnifiedId = "drip-test"; s.AuthToken = "fake"; s.OfflineMode = false; s.LastConfirmedServerXp = 0;
            s.DescentMigrationCompleted = true;
            s.DescentPendingStageCeremonies = new List<int> { 2, 3 };
            s.DescentLastStageDripDate = null;
            var stages = new List<int>();
            sync.StageCeremonyDue = stages.Add;
            var now = DateTime.UtcNow;
            sync.UtcNow = () => now;
            sync.MarkLoaded(null);

            handler.Body = "{\"user\":{}}";
            Assert.True(await sync.PushAsync("first"));
            Assert.Equal(new[] { 2 }, stages);
            Assert.Equal(new[] { 3 }, s.DescentPendingStageCeremonies);

            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("same day"));
            Assert.Equal(new[] { 2 }, stages); // a second sync the same day releases nothing

            s.DescentLastStageDripDate = null;
            handler.Body = "{"; // an unreadable reply cannot spend tomorrow's stage
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            await sync.PushAsync("malformed");
            Assert.Equal(new[] { 2 }, stages);
            Assert.Equal(new[] { 3 }, s.DescentPendingStageCeremonies);
        }
        finally
        {
            sync.Reset();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreDispatch.PostProvider) = old;
        }
    }

    private sealed class Reply : HttpMessageHandler
    {
        internal string Body = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
    }
}
