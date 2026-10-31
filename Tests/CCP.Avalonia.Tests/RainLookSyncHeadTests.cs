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

public sealed class RainLookSyncHeadTests
{
    private sealed class Fake : HttpMessageHandler
    {
        internal int Calls;
        internal HttpMethod? LastMethod;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            LastMethod = r.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task Only_current_account_pulls_once_and_panic_stops_the_client()
    {
        var s = CoreSettings.Current;
        var (id, offline) = (s.UnifiedId, s.OfflineMode);
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "dummy" : null;
        CoreSecrets.StoreProvider = (_, _) => { };
        var old = RainLookSyncHead.Client;
        using var http = new Fake();
        try
        {
            s.UnifiedId = "rain-test";
            s.AuthToken = "dummy";
            s.OfflineMode = false;
            RainLookSyncHead.Stop();
            RainLookSyncHead.Client = new RainLookSync(new HttpClient(http), new RainLookSync.Hooks
            {
                Identity = () => ("rain-test", "dummy"), Local = () => ("", false, null),
                Agreed = (_, _) => throw new Exception("404 must not agree"),
                Adopt = (_, _) => throw new Exception("404 must not adopt"),
                BaseUrl = "https://rain.test",
            });
            await RainLookSyncHead.PullAsync("other", default);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await RainLookSyncHead.PullAsync("rain-test", cancelled.Token);
            Assert.Equal(0, http.Calls);
            await RainLookSyncHead.PullAsync("rain-test", default);
            await RainLookSyncHead.PullAsync("rain-test", default);
            Assert.Equal(1, http.Calls);
            Assert.Contains(PanicSurfaces.All, s => s.Id == "rain-look-sync");
            foreach (var surface in PanicSurfaces.All)
                if (surface.Id == "rain-look-sync") surface.Stop(null);
            await RainLookSyncHead.PullAsync("rain-test", default);
            Assert.Equal(2, http.Calls);
        }
        finally
        {
            RainLookSyncHead.Stop();
            RainLookSyncHead.Client = old;
            (s.UnifiedId, s.OfflineMode) = (id, offline);
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Matching_validate_without_prizes_or_grants_still_syncs(bool emptyBlock, bool pushOwed)
        => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var s = CoreSettings.Current;
            var (id, offline) = (s.UnifiedId, s.OfflineMode);
            var oldGet = CoreSecrets.RetrieveProvider;
            var oldSet = CoreSecrets.StoreProvider;
            var old = RainLookSyncHead.Client;
            CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "dummy" : null;
            CoreSecrets.StoreProvider = (_, _) => { };
            using var http = new Fake();
            try
            {
                s.UnifiedId = "rain-test";
                s.OfflineMode = false;
                RainLookSyncHead.Stop();
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
                await Drain();
                Assert.Equal(0, http.Calls);
                RainLookSyncHead.ApplyPrizes("rain-test", "rain-test", prizes, "test");
                await Drain();
                Assert.Equal(1, http.Calls);
                Assert.Equal(pushOwed ? HttpMethod.Put : HttpMethod.Get, http.LastMethod);
                Assert.Equal(pushOwed, owed); // A 404 must leave an owed push intact.
            }
            finally
            {
                RainLookSyncHead.Stop();
                RainLookSyncHead.Client = old;
                (s.UnifiedId, s.OfflineMode) = (id, offline);
                CoreSecrets.RetrieveProvider = oldGet;
                CoreSecrets.StoreProvider = oldSet;
            }
        });
}
