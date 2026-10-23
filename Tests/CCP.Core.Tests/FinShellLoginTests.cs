using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

[Collection(SessionStatics.Name)] // CoreSettings.ServiceProvider and CoreDispatch.PostProvider are process-global
public sealed class FinShellLoginTests : IDisposable
{
    private readonly Func<SettingsService?>? _oldSettings = CoreSettings.ServiceProvider;
    private readonly Func<bool>? _oldLogin = CoreAccount.IsLoggedInProvider;
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly SettingsService _settings = new() { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-sync-").FullName;
    private readonly AppSettings _s;

    public FinShellLoginTests()
    {
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = k => secrets.GetValueOrDefault(k);
        CoreSecrets.StoreProvider = (k, v) => secrets[k] = v;
        CoreSettings.ServiceProvider = () => _settings;
        CoreAccount.IsLoggedInProvider = () => true;
        _s = _settings.Current;
        _s.UnifiedId = "test-account";
        _s.AuthToken = "fake";
        _s.OfflineMode = false;
        _s.LastConfirmedServerXp = 0;
        _s.PlayerLevel = 1; _s.PlayerXP = 0;
        _s.CompanionProgressData = new();
        _s.TotalConditioningMinutes = 10;
    }

    public void Dispose()
    {
        _settings.SealForReset();
        CoreSettings.ServiceProvider = _oldSettings;
        CoreAccount.IsLoggedInProvider = _oldLogin;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
        Directory.Delete(_dir, true);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal string Reply = "{}";
        internal readonly List<JObject> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
            return new(HttpStatusCode.OK) { Content = new StringContent(Reply) };
        }
    }

    [Fact]
    public async Task FirstPushOmitsUnknownProgress_ThenEchoesOnlyAdoptedFields_LogoutDisarmsThem()
    {
        var p = new AchievementProgress { TotalFlashImages = 7 };
        using var quests = new QuestService(null, _dir);
        var wire = new Wire { Reply = """{"user":{"stats":{"total_flashes":12},"achievements":["cloud-earned"]},"total_conditioning_minutes":30,"companion_progress":{"1":{"Level":3,"TotalXPEarned":900}},"reset_daily_quest":false,"reset_weekly_quest":false,"force_streak_override":false}""" };
        var saves = 0;
        var now = DateTime.UtcNow;
        var sync = new SyncPush(() => p.UnlockedAchievements, () => false, wire,
            progress: () => p, quests: () => quests, saveProgress: () => saves++) { UtcNow = () => now };
        sync.MarkLoaded(Array.Empty<string>());
        Assert.True(await sync.PushAsync("load"));
        var first = wire.Bodies[0];
        foreach (var key in new[] { "stats", "companion_progress", "total_conditioning_minutes" }) Assert.Null(first[key]);
        Assert.Equal(12, p.TotalFlashImages);
        Assert.True(p.IsUnlocked("cloud-earned"));
        Assert.Equal(30, _s.TotalConditioningMinutes);
        Assert.Equal(3, _s.CompanionProgressData[1].Level);
        Assert.Equal(1, saves);
        p.TotalFlashImages = 15;
        now = now.AddSeconds(31);
        Assert.True(await sync.PushAsync("heartbeat"));
        var next = wire.Bodies[1];
        Assert.Equal(new[] { "total_flashes" }, ((JObject)next["stats"]!).Properties().Select(v => v.Name));
        Assert.Equal(15, (int)next["stats"]!["total_flashes"]!);
        foreach (var flag in new[] { "reset_daily_quest", "reset_weekly_quest", "force_streak_override" }) Assert.False((bool)next[flag]!);
        Assert.Equal(30, (double)next["total_conditioning_minutes"]!);
        Assert.Equal(3, (int)next["companion_progress"]!["1"]!["Level"]!);
        sync.Reset(); sync.MarkLoaded(Array.Empty<string>()); now = now.AddSeconds(31);
        Assert.True(await sync.PushAsync("relogin"));
        Assert.Null(wire.Bodies[2]["stats"]);
        Assert.Null(wire.Bodies[2]["companion_progress"]);
    }

    [Fact]
    public async Task CloudProgressMergeHopsToTheUiThread()
    {
        var p = new AchievementProgress { TotalFlashImages = 7 };
        using var quests = new QuestService(null, _dir);
        var wire = new Wire { Reply = """{"user":{"stats":{"total_flashes":12},"achievements":["cloud-earned"]},"total_conditioning_minutes":30}""" };
        var saves = 0;
        var posted = new List<Action>();
        var oldPost = CoreDispatch.PostProvider;
        CoreDispatch.PostProvider = posted.Add;   // a Nudge push answers on the pool; nothing may touch progress there
        try
        {
            var sync = new SyncPush(() => p.UnlockedAchievements, () => false, wire,
                progress: () => p, quests: () => quests, saveProgress: () => saves++);
            sync.MarkLoaded(Array.Empty<string>());
            Assert.True(await sync.PushAsync("nudge"));
            Assert.Equal(7, p.TotalFlashImages);
            Assert.False(p.IsUnlocked("cloud-earned"));
            Assert.Equal(10, _s.TotalConditioningMinutes);
            Assert.Equal(0, saves);
            Assert.Single(posted);
            posted[0]();
            Assert.Equal(12, p.TotalFlashImages);
            Assert.True(p.IsUnlocked("cloud-earned"));
            Assert.Equal(30, _s.TotalConditioningMinutes);
            Assert.Equal(1, saves);
        }
        finally { CoreDispatch.PostProvider = oldPost; }
    }

    [Fact]
    public async Task SyncFlushesLocalDayLogAndCloudLiftIsNeverBookedAsTodaysUse()
    {
        var p = new AchievementProgress { TotalFlashImages = 5 };
        using var log = new FeatureDayLogService(() => p, Path.Combine(_dir, "day-log.json"));
        p.TotalFlashImages += 2;
        var wire = new Wire { Reply = """{"user":{"stats":{"total_flashes":100}},"total_conditioning_minutes":1000}""" };
        var now = DateTime.UtcNow;
        var sync = new SyncPush(() => p.UnlockedAchievements, () => false, wire,
            progress: () => p, dayLog: () => log) { UtcNow = () => now };
        sync.MarkLoaded(Array.Empty<string>());
        Assert.True(await sync.PushAsync("load"));
        Assert.Equal(2, (int)wire.Bodies[0]["stats"]!["feature_day_log"]![0]!["fl"]!);
        p.TotalFlashImages += 3;
        now = now.AddSeconds(31);
        Assert.True(await sync.PushAsync("local-use"));
        var day = wire.Bodies[1]["stats"]!["feature_day_log"]![0]!;
        Assert.Equal(5, (int)day["fl"]!);
        Assert.Null(day["cm"]);
    }

    [Fact]
    public void ServerDailyResetRedealsUnfinishedSlotsAndKeepsCompletedCards()
    {
        using var quests = new QuestService(null, _dir);
        var q = quests.Progress;
        Assert.True(q.DailyQuests.Count >= 2);
        q.DailyQuests[0].IsCompleted = true;
        var done = q.DailyQuests[0]; var unfinished = q.DailyQuests[1];
        CloudSyncProgress.QuestFlags(_s, JObject.Parse("""{"reset_daily_quest":true}"""), quests);
        Assert.Same(done, q.DailyQuests[0]);
        Assert.NotSame(unfinished, q.DailyQuests[1]);
    }

    [Fact]
    public void LocalConsentChoicesAreIndependentAndRevocationIsSent()
    {
        _s.AllowDiscordDm = true; _s.ShareProfilePicture = true; _s.PublicShareRealAvatar = false;
        _s.ShowOnlineStatus = false; _s.GoonShareAvatar = true; _s.GoonShareDiscordDm = false;
        JObject Body() => JObject.FromObject(SyncPush.Body(_s, null));
        var body = Body();
        foreach (var key in new[] { "allow_discord_dm", "share_profile_picture", "goon_share_avatar" }) Assert.True((bool)body[key]!);
        foreach (var key in new[] { "show_online_status", "public_share_avatar", "goon_share_dm" }) Assert.False((bool)body[key]!);
        _s.ShareProfilePicture = false;
        Assert.False((bool)Body()["share_profile_picture"]!);
        Assert.False((bool)Body()["public_share_avatar"]!);
        Assert.Null(body["goon_rich_presence"]);
    }

    [Theory]
    [InlineData(4, 50, 3, 900, 4, 50)] // higher level beats a lower level with more lifetime XP
    [InlineData(3, 500, 3, 900, 3, 900)]
    [InlineData(3, 900, 3, 500, 3, 900)]
    [InlineData(3, 900, 4, 50, 4, 50)]
    public void CompanionMergeMatchesWpf(int level, double xp, int cloudLevel, double cloudXp, int expectedLevel, double expectedXp)
    {
        _s.CompanionProgressData[1] = new CompanionProgress { Level = level, TotalXPEarned = xp };
        var reply = new JObject { ["companion_progress"] = new JObject { ["bad-id"] = new JObject(),
            ["1"] = JObject.FromObject(new CompanionProgress { Level = cloudLevel, TotalXPEarned = cloudXp }) } };
        CloudSyncProgress.Adopt(_s, reply, null, null);
        Assert.Equal(expectedLevel, _s.CompanionProgressData[1].Level);
        Assert.Equal(expectedXp, _s.CompanionProgressData[1].TotalXPEarned);
        Assert.Single(_s.CompanionProgressData);
    }

    [Fact]
    public void CloudHistoryUnionsPrunesAndPersists_WithoutAddingMobileTotalsToDesktop()
    {
        using var quests = new QuestService(null, _dir);
        var q = quests.Progress;
        q.DailyQuestCompletionDates.Clear(); q.QuestCompletionLog.Clear();
        q.DailyQuestCompletionDates.Add(DateTime.Today.AddDays(-100));
        q.TotalDailyQuestsCompleted = 7;
        var day = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        q.QuestCompletionLog.Add(new QuestLogEntry(day, "local"));
        var reply = JObject.Parse("""{"user":{"stats":{"total_flashes":20,"daily_quest_streak":12,"total_daily_quests_completed":10,"quest_completion_dates":[],"quest_completion_log":[]},"mobile_stats":{"total_daily_quests_completed":99,"total_xp_from_quests":700}}}""");
        reply["user"]!["stats"]!["quest_completion_dates"] = new JArray(day, day, "2999-01-01", "bad");
        reply["user"]!["stats"]!["quest_completion_log"] = new JArray(
            JObject.FromObject(new QuestLogEntry(day, "cloud")), JObject.FromObject(new QuestLogEntry(day, "cloud")),
            JObject.FromObject(new QuestLogEntry("2000-01-01", "old")));
        var p = new AchievementProgress { TotalFlashImages = 30 };
        CloudSyncProgress.Adopt(_s, reply, p, quests);
        Assert.Equal(30, p.TotalFlashImages);
        Assert.Equal(12, _s.DailyQuestStreak);
        Assert.Equal(DateTime.Today, Assert.Single(q.DailyQuestCompletionDates));
        Assert.Equal(new[] { "local", "cloud" }, q.QuestCompletionLog.Select(e => e.Q));
        Assert.Equal(10, q.TotalDailyQuestsCompleted);
        Assert.Equal(99, _s.MobileQuestDailyCompleted);
        Assert.Equal(700, _s.MobileQuestXP);
        Assert.Equal(10, (int)CloudSyncProgress.Body(_s, p, q)["total_daily_quests_completed"]);
        Assert.Contains("cloud", File.ReadAllText(Path.Combine(_dir, "quests.json")));
    }

    [Fact]
    public void ForceStreakCorrectionWinsOverHigherEcho()
    {
        using var quests = new QuestService(null, _dir);
        _s.DailyQuestStreak = 80;
        var reply = JObject.Parse("""{"force_streak_override":true,"streak_stats":{"daily_quest_streak":2,"total_daily_quests_completed":3},"user":{"stats":{"daily_quest_streak":99,"total_daily_quests_completed":100}}}""");
        CloudSyncProgress.QuestFlags(_s, reply, quests);
        CloudSyncProgress.Adopt(_s, reply, null, quests);
        Assert.Equal(2, _s.DailyQuestStreak);
        Assert.Equal(3, quests.Progress.TotalDailyQuestsCompleted);
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void DayKeysUseGregorianCalendarAndPreserveIsoDayAsWritten(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.True(CloudSyncProgress.TryDay("2026-08-26T23:00:00-10:00", out var day));
            Assert.Equal(new DateTime(2026, 8, 26), day);
            Assert.False(CloudSyncProgress.TryDay("Aug 26, 2026", out _));
            Assert.False(CloudSyncProgress.TryDay("2999-01-01", out _));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void QuestWireLogKeepsNewest400AndVideoMinutesRoundToOneDecimal()
    {
        var q = new QuestProgress();
        for (var i = 0; i < 405; i++) q.QuestCompletionLog.Add(new QuestLogEntry("2026-08-26", "q" + i));
        var body = CloudSyncProgress.Body(_s, new AchievementProgress { TotalVideoMinutes = 4.26 }, q);
        Assert.Equal(4.3, body["total_video_minutes"]);
        var log = Assert.IsType<List<Dictionary<string, string>>>(body["quest_completion_log"]);
        Assert.Equal(400, log.Count);
        Assert.Equal("q5", log[0]["q"]);
        Assert.Equal("q404", log[^1]["q"]);
    }

    [Fact]
    public void PhoneCoveredDayExtendsLoginStreakAndLifetimeCountersNeverRegress()
    {
        var p = new AchievementProgress { ConsecutiveDays = 10, LastLaunchDate = DateTime.Today.AddDays(-1),
            TotalBubblesPopped = 20, TotalXPEarned = 200, LifetimeSkillPointsSpent = 50 };
        var reply = JObject.Parse("""{"user":{"stats":{"consecutive_days":5,"total_bubbles_popped":30,"total_xp_earned":100,"lifetime_points_spent":70,"longest_session_minutes":12.5}}}""");
        reply["user"]!["stats"]!["last_streak_date"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        CloudSyncProgress.Adopt(_s, reply, p, null);
        Assert.Equal(11, p.ConsecutiveDays);
        Assert.Equal(DateTime.Today, p.LastLaunchDate);
        Assert.Equal(11, _s.CurrentStreak);
        Assert.Equal(30, p.TotalBubblesPopped);
        Assert.Equal(200, p.TotalXPEarned);
        Assert.Equal(70, p.LifetimeSkillPointsSpent);
        Assert.Equal(12.5, p.LongestSessionMinutes);
    }

    [Fact]
    public void ClaimedXpRaisesNoticeOnlyAfterSettlingAndNeverCreditsQuests()
    {
        var notices = new List<double>(); var questCredits = 0;
        void Notice(double amount) { Assert.Equal(100, _s.PlayerXP); notices.Add(amount); }
        void QuestCredit(double amount, string source) => questCredits++;
        ProgressionBank.Claimed += Notice; ProgressionBank.Awarded += QuestCredit;
        try
        {
            ProgressionBank.AddClaimed(0);
            ProgressionBank.AddClaimed(100);
            Assert.Equal(new double[] { 100 }, notices);
            Assert.Equal(0, questCredits);
        }
        finally { ProgressionBank.Claimed -= Notice; ProgressionBank.Awarded -= QuestCredit; }
    }
}
