using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Fact]
    public void QuestResetRepliesAreDispatchedAndAcknowledged() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        var daily = 0;
        var weekly = 0;
        sync.ResetDailyQuest = () => daily++;
        sync.ResetWeeklyQuest = () => weekly++;
        var previous = CoreDispatch.PostProvider;
        CoreDispatch.PostProvider = action => action();
        try
        {
            wire.SyncReply = """{"success":true,"reset_daily_quest":true,"reset_weekly_quest":true}""";
            Assert.True(await AccountSeed.LoadProfileAsync());
            Assert.Equal(1, daily);
            Assert.Equal(1, weekly);
            wire.SyncReply = """{"success":true}""";
            sync.UtcNow = () => T0.AddSeconds(31);
            Assert.True(await sync.PushAsync("reset ack"));
            var body = wire.Syncs.Last();
            Assert.False((bool)body["reset_daily_quest"]!);
            Assert.False((bool)body["reset_weekly_quest"]!);
            Assert.Equal(1, daily);
            Assert.Equal(1, weekly);
        }
        finally { CoreDispatch.PostProvider = previous; }
    });

    [Fact]
    public void ResetsRegenerateTheActualQuestBoards() => WithFreshInstall(async () =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sync-quests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        var old = AvApp.Quests;
        var dispatch = CoreDispatch.PostProvider;
        try
        {
            AvApp.Quests = quests;
            var before = new ActiveQuest("pop_parade_d");
            quests.Progress.DailyQuests = new List<ActiveQuest> { before };
            var beforeWeekly = quests.Progress.WeeklyQuest;
            quests.Progress.WeeklyQuestGeneratedAt = null;
            CoreDispatch.PostProvider = action => action();
            var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
            wire.SyncReply = """{"success":true,"reset_daily_quest":true,"reset_weekly_quest":true}""";
            Assert.True(await AccountSeed.LoadProfileAsync());
            var rerolled = Assert.Single(quests.Progress.DailyQuests);
            Assert.NotSame(before, rerolled);
            var weekly = Assert.IsType<ActiveQuest>(quests.Progress.WeeklyQuest);
            Assert.NotSame(beforeWeekly, weekly);
            wire.SyncReply = """{"success":true}""";
            sync.UtcNow = () => T0.AddSeconds(31);
            Assert.True(await sync.PushAsync("reset ack"));
            Assert.Same(rerolled, Assert.Single(quests.Progress.DailyQuests));
            Assert.Same(weekly, quests.Progress.WeeklyQuest);
        }
        finally
        {
            CoreDispatch.PostProvider = dispatch;
            AvApp.Quests = old;
            quests.Dispose();
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void ReplyFromAnEarlierSessionForSameAccountIsDropped() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        Assert.True(await AccountSeed.LoadProfileAsync());
        var calls = 0;
        sync.ResetDailyQuest = () => calls++;
        wire.Hold = new TaskCompletionSource();
        wire.SyncReply = """{"success":true,"reset_daily_quest":true}""";
        sync.UtcNow = () => T0.AddSeconds(31);
        var push = sync.PushAsync("reset");
        sync.Reset();
        sync.MarkLoaded(null);
        wire.Hold.SetResult();
        Assert.False(await push);
        Assert.Equal(0, calls);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedQuestResetsDoNotCrossLogoutOrAccountSwitch(bool switchAccount) => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        Assert.True(await AccountSeed.LoadProfileAsync());
        var queued = new Queue<Action>();
        var previous = CoreDispatch.PostProvider;
        var calls = 0;
        sync.ResetDailyQuest = () => calls++;
        sync.ResetWeeklyQuest = () => calls++;
        try
        {
            CoreDispatch.PostProvider = queued.Enqueue;
            wire.SyncReply = """{"success":true,"reset_daily_quest":true,"reset_weekly_quest":true}""";
            sync.UtcNow = () => T0.AddSeconds(31);
            Assert.True(await sync.PushAsync("reset"));
            Assert.Single(queued);
            if (switchAccount) CoreSettings.Current.UnifiedId = "u2";
            else
            {
                sync.Reset();
                sync.MarkLoaded(null); // even a new session for the same account cannot apply the old reply
            }
            queued.Dequeue()();
            Assert.Equal(0, calls);
        }
        finally { CoreDispatch.PostProvider = previous; }
    });
}
