using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Restack b164 (#3712, #3916, #4120, hand-ported onto the trunk's PostLoginAsync and SyncPush reply order):
/// cloud achievements restored silently into the requesting session only, quest files owned per account,
/// and the mobile quest ledger adopted from the sync reply.
/// </summary>
public sealed partial class AccountSeedTests
{
    [Fact]
    public void ProfileLoadAndSyncRestoreHistoricalAchievementsOnTheUiQueue() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1", "from-get"));
        wire.SyncReply = """{"success":true,"user":{"achievements":["from-post"]}}""";
        var queue = new Queue<Action>();
        CoreDispatch.PostProvider = queue.Enqueue;
        var restored = new HashSet<string>();
        sync.RestoreAchievements = ids => restored.UnionWith(ids);
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Empty(restored);
        while (queue.TryDequeue(out var restore)) restore();
        Assert.True(restored.SetEquals(new[] { "from-get", "from-post" }));
        sync.UtcNow = () => T0.AddMinutes(1);
        Assert.True(await sync.PushAsync("union"));
        Assert.Contains("from-post", wire.Syncs.Last()["achievements"]!.Values<string>());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedAchievementRestoreCannotCrossAccountOrSession(bool sameAccount) => WithFreshInstall(async () =>
    {
        var (_, sync, _) = SignIn("u1", L40Profile("u1", "old-account"));
        var queue = new Queue<Action>();
        CoreDispatch.PostProvider = queue.Enqueue;
        var restored = new List<string>();
        sync.RestoreAchievements = ids => restored.AddRange(ids);
        Assert.True(await AccountSeed.LoadProfileAsync());
        if (sameAccount) { sync.Reset(); sync.MarkLoaded(null); }
        else CoreSettings.Current.UnifiedId = "u2";
        while (queue.TryDequeue(out var restore)) restore();
        Assert.Empty(restored);
    });

    [Fact]
    public void InFlightSyncCannotRestoreAfterSameAccountRelogin() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        sync.MarkLoaded(null);
        wire.Hold = new TaskCompletionSource();
        wire.SyncReply = """{"success":true,"user":{"achievements":["old-session"]}}""";
        var restored = new List<string>();
        sync.RestoreAchievements = ids => restored.AddRange(ids);
        var push = sync.PushAsync("held");
        Assert.False(push.IsCompleted);
        sync.Reset();
        sync.MarkLoaded(null);
        wire.Hold.SetResult();
        Assert.False(await push);
        Assert.Empty(restored);
    });

    [Fact]
    public void SyncReplyReplacesTheMobileQuestLedger() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        sync.MarkLoaded(null);
        var s = CoreSettings.Current;
        (s.MobileQuestDailyCompleted, s.MobileQuestWeeklyCompleted, s.MobileQuestXP) = (9, 9, 9);
        wire.SyncReply = """{"success":true,"user":{"mobile_stats":{"total_daily_quests_completed":4,"total_weekly_quests_completed":2,"total_xp_from_quests":350}}}""";
        Assert.True(await sync.PushAsync("mobile"));
        Assert.Equal((4, 2, 350), (s.MobileQuestDailyCompleted, s.MobileQuestWeeklyCompleted, s.MobileQuestXP));
        wire.SyncReply = """{"success":true}""";
        sync.UtcNow = () => T0.AddMinutes(1);
        Assert.True(await sync.PushAsync("no ledger"));
        Assert.Equal((4, 2, 350), (s.MobileQuestDailyCompleted, s.MobileQuestWeeklyCompleted, s.MobileQuestXP));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LogoutAndNextProfileLoad_PreserveOnlyTheOwnersQuests(bool sameAccount) => WithFreshInstall(async () =>
    {
        var oldQuests = AvApp.Quests;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-quest-login-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        try
        {
            AvApp.Quests = quests;
            var (wire, _, _) = SignIn("a", profile: null);
            quests.Progress.TotalDailyQuestsCompleted = 17;
            await AccountSeed.Logout();
            Assert.Equal("a", quests.Progress.OwnerUnifiedId);
            Assert.Equal(17, quests.Progress.TotalDailyQuestsCompleted);
            Assert.Contains("\"OwnerUnifiedId\": \"a\"", File.ReadAllText(Path.Combine(dir, "quests.json")));

            var next = sameAccount ? "a" : "b";
            SignIn(next, profile: null, wire);
            // Even a failed GET must isolate the quest file before any cloud work.
            Assert.False(await AccountSeed.LoadProfileAsync());
            Assert.Equal(next, quests.Progress.OwnerUnifiedId);
            Assert.Equal(sameAccount ? 17 : 0, quests.Progress.TotalDailyQuestsCompleted);
        }
        finally { AvApp.Quests = oldQuests; quests.Dispose(); Directory.Delete(dir, true); }
    });

    [Fact]
    public void DeletionWipesTheQuestFileInsteadOfPreservingIt() => WithFreshInstall(async () =>
    {
        var oldQuests = AvApp.Quests;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-quest-delete-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        try
        {
            AvApp.Quests = quests;
            SignIn("a", profile: null);
            quests.StampOwner("a");
            quests.Progress.TotalDailyQuestsCompleted = 17;
            await AccountSeed.Logout(accountDeleted: true);
            Assert.Null(quests.Progress.OwnerUnifiedId);
            Assert.Equal(0, quests.Progress.TotalDailyQuestsCompleted);
        }
        finally { AvApp.Quests = oldQuests; quests.Dispose(); Directory.Delete(dir, true); }
    });

    [Fact]
    public void DirectSwitchWipesAnUnstampedQuestFileBeforeTheRead() => WithFreshInstall(async () =>
    {
        var oldQuests = AvApp.Quests;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-quest-switch-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var quests = new QuestService(null, dir);
        try
        {
            AvApp.Quests = quests;
            var (wire, _, _) = SignIn("a", profile: null);
            quests.Progress.TotalDailyQuestsCompleted = 17; // unstamped older file
            ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog.ClearIfOtherAccount("b");
            SignIn("b", profile: null, wire);
            Assert.False(await AccountSeed.LoadProfileAsync());
            Assert.Equal("b", quests.Progress.OwnerUnifiedId);
            Assert.Equal(0, quests.Progress.TotalDailyQuestsCompleted);
        }
        finally { AvApp.Quests = oldQuests; quests.Dispose(); Directory.Delete(dir, true); }
    });
}
