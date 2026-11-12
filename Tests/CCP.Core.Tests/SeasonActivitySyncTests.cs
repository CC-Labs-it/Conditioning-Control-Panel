using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Core.Tests;
[Collection(SessionStatics.Name)]
public class SeasonActivitySyncTests
{
    private sealed class Reply : HttpMessageHandler
    {
        public JObject? Body;
        public Action? OnSend;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            OnSend?.Invoke();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"user\":{\"stats\":{\"consecutive_days\":7,\"last_streak_date\":\"2026-07-07T23:00:00-07:00\"}}}") };
        }
    }
    [Fact]
    public async Task SyncCarriesOnlyTheKnownStreakPairAndDeliversCloudReply()
    {
        var previous = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        var http = new Reply();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, http);
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.IsLoggedInProvider = () => true;
            var s = service.Current;
            s.UnifiedId = "streak-test";
            s.OfflineMode = false;
            s.LastConfirmedServerXp = 0;
            sync.LoginStreak = () => (4, new DateTime(2026, 7, 4));
            JObject? received = null;
            sync.LoginStreakReceived = (id, stats) => { Assert.Equal("streak-test", id); received = stats; };
            sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("test"));
            var stats = Assert.IsType<JObject>(http.Body!["stats"]);
            Assert.Equal(2, stats.Count);
            Assert.Equal(4, (int)stats["consecutive_days"]!);
            Assert.Equal("2026-07-04", (string?)stats["last_streak_date"]);
            Assert.Equal(7, (int)received!["consecutive_days"]!);
            Assert.Equal(JTokenType.String, received["last_streak_date"]!.Type);
            Assert.Equal("2026-07-07T23:00:00-07:00", (string?)received["last_streak_date"]);
            Assert.Equal("streak-test", sync.LastLoginStreakReply?.Account);
            sync.Reset();
            Assert.Null(sync.LastLoginStreakReply);
        }
        finally
        {
            sync.Reset();
            service.SealForReset();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider) = previous;
        }
    }

    [Fact]
    public async Task ReplyFromPriorSessionIsDroppedEvenWhenAccountIdMatches()
    {
        var previous = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        var http = new Reply();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, http);
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.IsLoggedInProvider = () => true;
            service.Current.UnifiedId = "same-account";
            service.Current.OfflineMode = false;
            service.Current.LastConfirmedServerXp = 0;
            sync.MarkLoaded(Array.Empty<string>());
            http.OnSend = () => { sync.Reset(); sync.MarkLoaded(Array.Empty<string>()); };
            Assert.False(await sync.PushAsync("test"));
            Assert.Null(sync.LastLoginStreakReply);
        }
        finally
        {
            sync.Reset();
            service.SealForReset();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider) = previous;
        }
    }
}
