using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public class RecoveredProfileReconcileTests
{
    [Theory]
    [InlineData(false, "2025-01", 1, 0, 10, true)]
    [InlineData(true, "2026-01", 1, 0, 1, false)]
    [InlineData(true, "2025-01", 20, 1000000, 20, false)]
    [InlineData(true, "2025-01", 10, 1000000, 10, false)]
    [InlineData(true, "2025-01", 1, 0, 10, false)]
    public async Task RecoveryGuardsPushAndPersistsBeforeClearing(bool readOk, string season, int serverLevel, int serverXp, int expectedLevel, bool markerExpected)
    {
        using var profile = new PrivateProfile();
        var path = profile.Path;
        var marker = path + ".restored";
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var settings = new SettingsService(path);
        var s = settings.Current;
        s.UnifiedId = "recover-test"; s.AuthToken = "fake"; s.OfflineMode = false;
        s.PlayerLevel = 10; s.PlayerXP = 0; s.CurrentSeason = "2025-01";
        s.LastConfirmedServerXp = 0; s.UnlockedSkills.Add("keep");
        s.SeasonalStreakRecoveryUsed = true; s.CurrentStreak = 8; s.LastStreakDate = new DateTime(2025, 1, 1);
        s.DailyQuestStreak = 7; s.LastDailyQuestDate = new DateTime(2025, 1, 2);
        s.LastPerfectWeekStreakAwarded = 7; s.StreakFixCharges = 3;
        Assert.True(settings.TrySaveImmediate(true));
        settings.SealForReset();
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o") + "|backup|2025-01");
        settings = new SettingsService(path) { SaveDebounceDueTimeMilliseconds = int.MaxValue };
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var handler = new Handler(readOk, season, serverLevel, serverXp, path, marker);
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        var notifications = 0;
        sync.RecoveredProgressionChanged = () => notifications++;
        sync.MarkLoaded(null);
        try
        {
            Assert.Equal(readOk, await sync.PushAsync("test"));
            Assert.Equal(readOk && (season == "2026-01" || serverXp == 1000000) ? 1 : 0, notifications);
            Assert.Equal(markerExpected, File.Exists(marker));
            Assert.Equal(markerExpected, settings.RestoredFromBackup);
            Assert.Equal(expectedLevel, settings.Current.PlayerLevel);
            Assert.Contains("keep", settings.Current.UnlockedSkills);
            var rollover = readOk && season == "2026-01";
            Assert.Equal(!rollover, settings.Current.SeasonalStreakRecoveryUsed);
            Assert.Equal(rollover ? 0 : 8, settings.Current.CurrentStreak);
            Assert.Equal(rollover ? null : new DateTime(2025, 1, 1), settings.Current.LastStreakDate);
            Assert.Equal(rollover ? 0 : 7, settings.Current.DailyQuestStreak);
            Assert.Equal(rollover ? null : new DateTime(2025, 1, 2), settings.Current.LastDailyQuestDate);
            Assert.Equal(rollover ? 0 : 7, settings.Current.LastPerfectWeekStreakAwarded);
            Assert.Equal(3, settings.Current.StreakFixCharges);
            Assert.Equal(readOk ? 1 : 0, handler.Posts);
            if (readOk)
            {
                Assert.True(settings.HasPendingSave); // the successful push arms a delayed save
                Assert.False(handler.MarkerAtPost);
                Assert.Equal(expectedLevel, handler.LevelAtPost);
                Assert.Contains("keep", handler.SkillsAtPost!);
                if (rollover)
                {
                    var saved = JObject.Parse(File.ReadAllText(path));
                    Assert.False(saved.Value<bool>("SeasonalStreakRecoveryUsed"));
                    Assert.Equal(0, saved.Value<int>("CurrentStreak"));
                    Assert.True(saved["LastStreakDate"] == null || saved["LastStreakDate"]!.Type == JTokenType.Null);
                    Assert.Equal(0, saved.Value<int>("DailyQuestStreak"));
                    Assert.True(saved["LastDailyQuestDate"] == null || saved["LastDailyQuestDate"]!.Type == JTokenType.Null);
                    Assert.Equal(0, saved.Value<int>("LastPerfectWeekStreakAwarded"));
                    Assert.Equal(3, saved.Value<int>("StreakFixCharges"));
                }
            }
            settings.SealForReset();
            Assert.False(settings.HasPendingSave);
        }
        finally
        {
            sync.Reset(); settings.SealForReset(); CoreSettings.ServiceProvider = oldSettings; CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    [Fact]
    public async Task FailedSaveLeavesMarkerAndDoesNotPush()
    {
        using var profile = new PrivateProfile();
        var path = profile.Path;
        var marker = path + ".restored";
        var oldSettings = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var initial = new SettingsService(path);
        initial.Current.UnifiedId = "recover-test"; initial.Current.AuthToken = "fake";
        initial.Current.OfflineMode = false; Assert.True(initial.TrySaveImmediate(true));
        initial.SealForReset();
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o") + "|backup|2025-01");
        var settings = new SettingsService(path);
        settings.SealForReset();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        var handler = new Handler(true, "2025-01", 1, 0, path, marker);
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        sync.MarkLoaded(null);
        try
        {
            Assert.False(await sync.PushAsync("test"));
            Assert.True(settings.RestoredFromBackup);
            Assert.True(File.Exists(marker));
            Assert.Equal(0, handler.Posts);
        }
        finally
        {
            sync.Reset(); CoreSettings.ServiceProvider = oldSettings; CoreAccount.IsLoggedInProvider = oldLogin;
        }
    }

    // Other tests can leave delayed saves against CorePaths.UserData. A private profile keeps
    // those writes from replacing the recovery fixture between its save and reload.
    private sealed class PrivateProfile : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("ccp-reconcile-").FullName;
        internal string Path => System.IO.Path.Combine(_directory, "settings.json");
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class Handler(bool ok, string season, int level, int xp, string path, string marker) : HttpMessageHandler
    {
        internal int Posts; internal bool MarkerAtPost; internal int LevelAtPost; internal string? SkillsAtPost;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(ok ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent(ok ? $"{{\"user\":{{\"unified_id\":\"recover-test\",\"level\":{level},\"xp\":{xp},\"current_season\":\"{season}\"}}}}" : "{}") });
            Posts++; MarkerAtPost = File.Exists(marker);
            var saved = JObject.Parse(File.ReadAllText(path));
            LevelAtPost = saved.Value<int>("PlayerLevel"); SkillsAtPost = saved["UnlockedSkills"]?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
