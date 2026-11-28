using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public class SyncProgressionReplyTests
{
    [Theory]
    [InlineData(2, 1000, true)]
    [InlineData(1, 1000, false)]
    public void ClampDefendsEmptyProfilesAndLowersTheWatermark(int serverLevel, int serverXp, bool clamp)
    {
        var s = new AppSettings { UnifiedId = "clamp-test", CurrentSeason = "2025-01", PlayerLevel = 80, PlayerXP = 0 };
        var before = ProfileAdopt.TotalXp(s);
        s.LastConfirmedServerXp = before;
        s.LastConfirmedServerXpAccount = s.UnifiedId;
        s.LastConfirmedServerXpSeason = s.CurrentSeason;
        var reply = JObject.Parse($"{{\"user\":{{\"level\":{serverLevel},\"xp\":{serverXp},\"current_season\":\"2025-01\"}}}}");
        ProfileAdopt.ApplySyncResponse(s, reply, new DateTime(2025, 1, 1));
        Assert.Equal(clamp ? serverLevel : 80, s.PlayerLevel);
        Assert.Equal(clamp ? serverXp : before, ProfileAdopt.TotalXp(s));
        Assert.Equal(clamp ? serverXp : before, ProfileAdopt.ActiveXpWatermark(s));
    }

    [Theory]
    [InlineData(75000, false)]
    [InlineData(75001, true)]
    public void ClampRequiresMoreThanSeventyFiveThousandXp(double gap, bool clamp)
    {
        var s = new AppSettings { PlayerLevel = 2, PlayerXP = 200 + gap };
        var reply = JObject.Parse("{\"user\":{\"level\":2,\"xp\":1000}}");
        ProfileAdopt.ApplySyncResponse(s, reply, new DateTime(2025, 1, 1));
        Assert.Equal(clamp ? 1000 : 1000 + gap, ProfileAdopt.TotalXp(s));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplyProgressionNotifiesOnlyTheRequestingSession(bool logoutBeforeDispatch)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldPost = CoreDispatch.PostProvider;
        var queue = new List<Action>();
        var settings = new SettingsService();
        var s = settings.Current;
        s.UnifiedId = "sync-progress-test"; s.AuthToken = "fake"; s.OfflineMode = false;
        s.CurrentSeason = "2025-01"; s.PlayerLevel = 80; s.PlayerXP = 0;
        s.LastConfirmedServerXp = 0; s.PendingDescentMigrationChoice = null;
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreDispatch.PostProvider = action => queue.Add(action);
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new Reply());
        var seen = 0;
        sync.SyncProgressionChanged = () => { Assert.Equal(2, s.PlayerLevel); seen++; };
        sync.MarkLoaded(null);
        try
        {
            Assert.True(await sync.PushAsync("test"));
            Assert.Equal(2, s.PlayerLevel);
            Assert.Single(queue);
            if (logoutBeforeDispatch) sync.Reset();
            foreach (var action in queue.ToArray()) action();
            Assert.Equal(logoutBeforeDispatch ? 0 : 1, seen);
        }
        finally
        {
            sync.Reset(); CoreDispatch.PostProvider = oldPost;
            CoreSettings.ServiceProvider = oldSettings; CoreAccount.IsLoggedInProvider = oldLogin;
            SettingsService.DisarmAllPendingSaves();
        }
    }

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 1)]
    public async Task HigherXpAndAcceptedResetNotifyTheShell(bool reset, int expectedLevel)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldPost = CoreDispatch.PostProvider;
        var settings = new SettingsService();
        var s = settings.Current;
        s.UnifiedId = "sync-progress-test"; s.AuthToken = "fake"; s.OfflineMode = false;
        s.CurrentSeason = "2025-01"; s.PlayerLevel = 10; s.PlayerXP = 0;
        s.LastConfirmedServerXp = 0; s.PendingDescentMigrationChoice = null;
        s.DescentMigrationCompleted = false;
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreDispatch.PostProvider = action => action();
        var json = reset ? "{\"level_reset\":true,\"user\":{\"level\":1,\"xp\":0,\"current_season\":\"2025-01\"}}"
            : "{\"user\":{\"level\":20,\"xp\":200000,\"current_season\":\"2025-01\"}}";
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new Reply(json));
        sync.UtcNow = () => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var seen = 0;
        sync.SyncProgressionChanged = () => seen++;
        sync.MarkLoaded(null);
        try
        {
            Assert.True(await sync.PushAsync("test"));
            Assert.Equal(expectedLevel, s.PlayerLevel);
            Assert.Equal(1, seen);
        }
        finally
        {
            sync.Reset(); CoreDispatch.PostProvider = oldPost;
            CoreSettings.ServiceProvider = oldSettings; CoreAccount.IsLoggedInProvider = oldLogin;
            SettingsService.DisarmAllPendingSaves();
        }
    }

    private sealed class Reply(string? json = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json ?? "{\"user\":{\"level\":2,\"xp\":1000,\"current_season\":\"2025-01\"}}") });
    }
}
