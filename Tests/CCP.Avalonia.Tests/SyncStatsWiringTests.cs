using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    // WPF ProfileSyncService.cs:1574-1610,2031-2051: the head's sync reaches its lifetime and quest stores both ways.
    [Fact]
    public void HeadSyncMergesReplyStatsIntoItsStoresAndSendsThemBack() => WithFreshInstall(async () =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sync-stats-head-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var prop = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;
        var (oldEngine, oldQuests, previous) = (prop.GetValue(null), AvApp.Quests, CoreDispatch.PostProvider);
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            prop.SetValue(null, engine);
            AvApp.Quests = new QuestService(null, dir);
            CoreDispatch.PostProvider = action => action();
            var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
            wire.SyncReply = """{"success":true,"user":{"stats":{"total_bubbles_popped":77,"total_weekly_quests_completed":5}}}""";
            Assert.True(await AccountSeed.LoadProfileAsync());
            Assert.Null(wire.Syncs.Last()["stats"]);
            Assert.Equal(77, engine.Progress.TotalBubblesPopped);
            Assert.Equal(5, AvApp.Quests.Progress.TotalWeeklyQuestsCompleted);
            wire.SyncReply = """{"success":true}""";
            sync.UtcNow = () => T0.AddSeconds(31);
            Assert.True(await sync.PushAsync("stats"));
            var body = wire.Syncs.Last();
            Assert.Equal(77, (int)body["stats"]!["total_bubbles_popped"]!);
            Assert.Equal(5, (int)body["stats"]!["total_weekly_quests_completed"]!);
            Assert.Equal(JTokenType.Array, body["stats"]!["feature_day_log"]!.Type);
            Assert.False((bool)body["force_streak_override"]!);
        }
        finally
        {
            CoreDispatch.PostProvider = previous;
            prop.SetValue(null, oldEngine);
            AvApp.Quests = oldQuests;
            Directory.Delete(dir, true);
        }
    });
}
