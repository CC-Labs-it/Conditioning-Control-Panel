using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
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
}
