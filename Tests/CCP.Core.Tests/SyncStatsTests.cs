using System;
using System.Collections.Generic;
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

/// <summary>WPF ProfileSyncService.cs:1574-1610 (outbound stats), 1802-1807 and 3727-3772 (force streak override),
/// 2031-2051 and 3448-3721 (inbound stats merge).</summary>
[Collection(SessionStatics.Name)]
public sealed class SyncStatsTests
{
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void MergeTakesHigherLifetimeCountersAndUnionsQuestHistory()
    {
        var today = DateTime.Today;
        var s = new AppSettings { DailyQuestStreak = 2, LastDailyQuestDate = today.AddDays(-3) };
        s.HighestStreak = 4;
        var p = new AchievementProgress { TotalBubblesPopped = 50, TotalFlashImages = 900, LongestSessionMinutes = 10, LifetimeSkillPointsSpent = 5 };
        var q = new QuestProgress { TotalDailyQuestsCompleted = 9, DailyQuestCompletionDates = { today.AddDays(-1) } };
        q.QuestCompletionLog.Add(new QuestLogEntry(Day(today.AddDays(-1)), "a"));
        var recalc = 0;
        var stats = JObject.Parse($$"""
            {"total_bubbles_popped":70,"total_flashes":12,"longest_session_minutes":12.5,"lifetime_points_spent":"300",
             "highest_streak":9,"total_spiral_minutes":3.5,"daily_quest_streak":6,"last_daily_quest_date":"{{Day(today)}}",
             "quest_completion_dates":["{{Day(today)}}","{{Day(today.AddDays(-1))}}","{{Day(today.AddDays(-200))}}"],
             "quest_completion_log":[{"d":"{{Day(today)}}","q":"b"},{"d":"{{Day(today.AddDays(-1))}}","q":"a"}],
             "total_daily_quests_completed":4,"total_weekly_quests_completed":3,"total_xp_from_quests":120,
             "daily_quests_completed_today":2,"daily_completion_reset_date":"{{Day(today)}}"}
            """);

        Assert.True(SyncStats.Merge(s, p, q, () => recalc++, stats, forceOverride: false));

        Assert.Equal(70, p.TotalBubblesPopped);
        Assert.Equal(900, p.TotalFlashImages);                  // never lowered
        Assert.Equal(12.5, p.LongestSessionMinutes);
        Assert.Equal(300, p.LifetimeSkillPointsSpent);
        Assert.Equal(3.5, p.TotalSpiralMinutes);
        Assert.Equal(9, s.HighestStreak);
        Assert.Equal(6, s.DailyQuestStreak);
        Assert.Equal(today, s.LastDailyQuestDate);
        Assert.Equal(new[] { today.AddDays(-1), today }, q.DailyQuestCompletionDates.OrderBy(d => d));
        Assert.Equal(1, recalc);
        Assert.Equal(2, q.QuestCompletionLog.Count);
        Assert.Equal(9, q.TotalDailyQuestsCompleted);
        Assert.Equal(3, q.TotalWeeklyQuestsCompleted);
        Assert.Equal(120, q.TotalXPFromQuests);
        Assert.Equal(2, q.GetDailyQuestsCompletedToday());
    }

    [Fact]
    public void ForceOverrideAdoptsLowerQuestValuesAndTheMergeLeavesThemAlone()
    {
        var today = DateTime.Today;
        var s = new AppSettings { DailyQuestStreak = 30, LastDailyQuestDate = today };
        var q = new QuestProgress { TotalDailyQuestsCompleted = 90, TotalXPFromQuests = 5000, DailyQuestCompletionDates = { today, today.AddDays(-1) } };
        var streak = JObject.Parse($$"""{"daily_quest_streak":3,"last_daily_quest_date":"{{Day(today.AddDays(-2))}}","quest_completion_dates":["{{Day(today.AddDays(-2))}}"],"total_daily_quests_completed":7,"total_weekly_quests_completed":1,"total_xp_from_quests":40}""");
        SyncStats.ApplyForceOverride(s, q, streak);
        Assert.Equal(3, s.DailyQuestStreak);
        Assert.Equal(today.AddDays(-2), s.LastDailyQuestDate);
        Assert.Equal(new[] { today.AddDays(-2) }, q.DailyQuestCompletionDates);
        Assert.Equal(7, q.TotalDailyQuestsCompleted);
        Assert.Equal(40, q.TotalXPFromQuests);
        var stats = JObject.Parse("""{"daily_quest_streak":30,"total_daily_quests_completed":90,"total_bubbles_popped":5}""");
        var p = new AchievementProgress();
        Assert.True(SyncStats.Merge(s, p, q, null, stats, forceOverride: true));
        Assert.Equal(5, p.TotalBubblesPopped);
        Assert.Equal(3, s.DailyQuestStreak);
        Assert.Equal(7, q.TotalDailyQuestsCompleted);
    }

    [Fact]
    public async Task SyncReplyMergesStatsOnTheUiQueueThenEveryPushCarriesThemWithTheOverrideAck()
    {
        var old = (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreAccount.IsLoggedInProvider);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sync-stats-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        var queue = new Queue<Action>();
        var handler = new Wire();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler);
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreDispatch.PostProvider = queue.Enqueue;
            var s = service.Current;
            (s.UnifiedId, s.AuthToken, s.OfflineMode, s.LastConfirmedServerXp) = ("stats-user", "tok", false, 0);
            s.DailyQuestStreak = 8;
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            engine.Progress.TotalBubblesPopped = 3;
            var quests = new QuestService(null, dir);
            quests.Progress.TotalWeeklyQuestsCompleted = 0;
            sync.StatsEngine = () => engine;
            sync.StatsQuests = () => quests;
            var now = DateTime.UtcNow;
            sync.UtcNow = () => now;
            sync.MarkLoaded(null);

            handler.Reply = """{"success":true,"force_streak_override":true,"streak_stats":{"daily_quest_streak":2,"total_weekly_quests_completed":4},"user":{"stats":{"total_bubbles_popped":40,"daily_quest_streak":99}}}""";
            Assert.True(await sync.PushAsync("first"));
            Assert.Null(handler.Bodies[0]["stats"]);                   // nothing known before the first reply
            Assert.Null(handler.Bodies[0]["force_streak_override"]);
            Assert.Equal(3, engine.Progress.TotalBubblesPopped);      // queued for the UI thread
            Assert.Single(queue);
            queue.Dequeue()();
            Assert.Equal(40, engine.Progress.TotalBubblesPopped);
            Assert.Equal(2, s.DailyQuestStreak);                       // the override, not the take-higher 99
            Assert.Equal(4, quests.Progress.TotalWeeklyQuestsCompleted);

            handler.Reply = """{"success":true}""";
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("second"));
            var stats = (JObject)handler.Bodies[1]["stats"]!;
            Assert.Equal(40, (int)stats["total_bubbles_popped"]!);
            Assert.Equal(2, (int)stats["daily_quest_streak"]!);
            Assert.Equal(4, (int)stats["total_weekly_quests_completed"]!);
            Assert.Equal(JTokenType.Array, stats["quest_completion_log"]!.Type);
            Assert.False((bool)handler.Bodies[1]["force_streak_override"]!);

            // A reply queued for one session never lands in the next one.
            handler.Reply = """{"success":true,"user":{"stats":{"total_bubbles_popped":500}}}""";
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("third"));
            sync.Reset();
            sync.MarkLoaded(null);
            while (queue.TryDequeue(out var a)) a();
            Assert.Equal(40, engine.Progress.TotalBubblesPopped);
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            handler.Reply = """{"success":true}""";
            Assert.True(await sync.PushAsync("after relogin"));
            Assert.Null(handler.Bodies[3]["stats"]);                   // logout forgets the known stats
        }
        finally
        {
            sync.Reset();
            service.SealForReset();
            (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreAccount.IsLoggedInProvider) = old;
            Directory.Delete(dir, true);
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal readonly List<JObject> Bodies = new();
        internal string Reply = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/v2/user/sync", request.RequestUri!.AbsolutePath);
            Bodies.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply) };
        }
    }
}
