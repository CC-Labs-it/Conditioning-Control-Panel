using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedReadStreakReplyKeepsItsSessionAndNeverResetsQuests(int scenario) => WithFreshInstall(async () =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-recovery-streak-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var previous = CoreDispatch.PostProvider;
        try
        {
            var (wire, sync, _) = SignIn("u1", null);
            var s = CoreSettings.Current;
            s.PlayerLevel = 2;
            s.StreakShieldsRemaining = 1;
            s.UnlockedSkills.Add("good_girl_streak");
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            var today = DateTime.Today;
            var p = engine.Progress;
            p.LastLaunchDate = today.AddDays(-3);
            p.ConsecutiveDays = 5;
            Assert.False(LoginStreakHead.Update(p, s, today, false));
            Assert.True(p.PendingStreakBreak);
            var queued = new Queue<Action>();
            CoreDispatch.PostProvider = queued.Enqueue;
            var resets = 0;
            sync.ResetDailyQuest = () => resets++;
            sync.ResetWeeklyQuest = () => resets++;
            sync.ApplyLoginStreak = stats => LoginStreakHead.ApplyCloud(engine, s, stats, today);
            wire.SyncReply = "{\"success\":true,\"reset_daily_quest\":true,\"reset_weekly_quest\":true,\"user\":{\"stats\":{\"consecutive_days\":7,\"last_streak_date\":\""
                + today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "\"}}}";
            Assert.True(await sync.PushAfterFailedReadAsync());
            Assert.False(sync.Loaded);
            Assert.Single(queued);
            if (scenario == 1) { sync.Reset(); sync.MarkLoaded(null); }
            if (scenario == 2) s.UnifiedId = "u2";
            if (scenario == 3) sync.MarkLoaded(null);
            queued.Dequeue()();
            Assert.Equal(0, resets);
            Assert.Equal(1, s.StreakShieldsRemaining);
            if (scenario is 0 or 3)
            {
                Assert.False(p.PendingStreakBreak);
                Assert.Equal(7, p.ConsecutiveDays);
                Assert.Equal(today, p.LastLaunchDate);
            }
            else
            {
                Assert.True(p.PendingStreakBreak);
                Assert.Equal(5, p.ConsecutiveDays);
            }
        }
        finally
        {
            CoreDispatch.PostProvider = previous;
            Directory.Delete(dir, true);
        }
    });
}
