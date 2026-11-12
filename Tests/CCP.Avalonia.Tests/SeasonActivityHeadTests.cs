using System;
using System.IO;
using System.Threading;
using System.Linq;
using CCP.Avalonia.Testing;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class SeasonActivityHeadTests
{
    [Fact]
    public void StartupAndRolloverPersistActivity() => AvaloniaTestDispatcher.Run(() =>
    {
        var root = CorePaths.UserData;
        var original = Directory.GetFiles(root, "settings*").ToDictionary(Path.GetFileName, File.ReadAllBytes);
        try { Exercise(); }
        finally
        {
            foreach (var file in Directory.GetFiles(root, "settings*")) File.Delete(file);
            foreach (var item in original) File.WriteAllBytes(Path.Combine(root, item.Key!), item.Value);
        }
        Assert.Equal(original.Keys.OrderBy(x => x), Directory.GetFiles(root, "settings*").Select(Path.GetFileName).OrderBy(x => x));
        foreach (var item in original) Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(root, item.Key!)));
    });

    private static bool ActivitySaved(string path, string day)
    {
        try { return (JObject.Parse(File.ReadAllText(path))["SeasonActiveDays"] as JArray)?.Values<string>().Contains(day) == true; }
        catch (IOException) { return false; }
    }

    private static void Exercise()
    {
        var settings = new SettingsService();
        settings.Current.SeasonActiveDays.Clear();
        settings.Current.SeasonPeakStreak = 0;
        settings.Current.OfflineMode = true;
        var achievementPath = Path.Combine(CorePaths.UserData, "season-test-" + Guid.NewGuid().ToString("N") + ".json");
        var engine = new AchievementEngine(new AchievementStore(achievementPath));
        var previousXp = CoreProgression.AddXPProvider;
        var awarded = 0.0;
        var popups = new System.Collections.Generic.List<(int Days, int Xp)>(); var moments = new System.Collections.Generic.List<(string, int)>();
        CoreProgression.AddXPProvider = (xp, _) => awarded += xp;
        var now = new DateTime(2026, 7, 3, 23, 59, 0, DateTimeKind.Utc);
        try
        {
            var check = App.StartSeasonActivity(settings, engine, () => now, bonusPopup: (days, xp) => popups.Add((days, xp)), streakMoment: (name, days) => moments.Add((name, days)));
            Assert.Equal(new[] { "2026-07-03" }, settings.Current.SeasonActiveDays);
            Assert.Equal(1, engine.Progress.ConsecutiveDays);
            Assert.Equal(1, settings.Current.SeasonPeakStreak);
            Assert.Equal(1, new AchievementStore(achievementPath).Load().ConsecutiveDays);
            engine.Progress.ConsecutiveDays = 7;
            check();
            Assert.Equal(7, settings.Current.SeasonPeakStreak);
            var path = Path.Combine(CorePaths.UserData, "settings.json");
            Assert.True(SpinWait.SpinUntil(() => ActivitySaved(path, "2026-07-03"), TimeSpan.FromSeconds(10)));
            engine.Progress.ConsecutiveDays = 9;
            check();
            Assert.Equal(9, settings.Current.SeasonPeakStreak);
            engine.Progress.ConsecutiveDays = 1;
            now = now.AddMinutes(2);
            if (now.ToLocalTime().Date == engine.Progress.LastLaunchDate.Date) { check(); Assert.Single(settings.Current.SeasonActiveDays); now = DateTime.SpecifyKind(engine.Progress.LastLaunchDate.Date.AddDays(1).AddHours(12), DateTimeKind.Local).ToUniversalTime(); }
            check();
            Assert.True(SpinWait.SpinUntil(() => ActivitySaved(path, "2026-07-04"), TimeSpan.FromSeconds(10)));
            var loaded = new SettingsService();
            Assert.Equal(new[] { "2026-07-03", "2026-07-04" }, loaded.Current.SeasonActiveDays);
            Assert.Equal(9, loaded.Current.SeasonPeakStreak);
            if (engine.Progress.ConsecutiveDays == 1)
            {
                var localNoon = engine.Progress.LastLaunchDate.Date.AddDays(1).AddHours(12);
                now = DateTime.SpecifyKind(localNoon, DateTimeKind.Local).ToUniversalTime();
                check();
            }
            Assert.Equal(2, engine.Progress.ConsecutiveDays);
            Assert.Equal(2, new AchievementStore(achievementPath).Load().ConsecutiveDays);
            Assert.Equal(50, awarded);
            Assert.Equal(new[] { (2, 50) }, popups); Assert.Equal(new[] { ("streakKept", 2) }, moments);

            now = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
            settings.Current.UnifiedId = "streak-test";
            settings.Current.OfflineMode = false;
            settings.Current.UnlockedSkills = new() { "good_girl_streak" };
            settings.Current.StreakShieldsRemaining = 1;
            engine.Progress.ConsecutiveDays = 4;
            engine.Progress.LastLaunchDate = now.ToLocalTime().Date.AddDays(-4);
            var sync = new SyncPush(() => null, () => false);
            var checkGap = App.StartSeasonActivity(settings, engine, () => now, sync);
            checkGap();
            Assert.True(engine.Progress.PendingStreakBreak);
            Assert.Equal(1, settings.Current.StreakShieldsRemaining);
            var cloud = JObject.Parse("{\"consecutive_days\":7,\"last_streak_date\":\"2026-07-07\"}");
            sync.LoginStreakReceived!("other", cloud);
            Dispatcher.UIThread.RunJobs();
            Assert.True(engine.Progress.PendingStreakBreak);
            sync.LoginStreakReceived!("streak-test", cloud);
            sync.Reset();
            Dispatcher.UIThread.RunJobs();
            Assert.True(engine.Progress.PendingStreakBreak); // queued answer from the old session
            sync.LoginStreakReceived!("streak-test", cloud);
            Dispatcher.UIThread.RunJobs();
            Assert.False(engine.Progress.PendingStreakBreak);
            Assert.Equal(8, engine.Progress.ConsecutiveDays);
            Assert.Equal(1, settings.Current.StreakShieldsRemaining);
            Assert.Equal(200, awarded);
            now = now.AddDays(4);
            checkGap();
            Assert.True(engine.Progress.PendingStreakBreak);
            Assert.Equal(1, settings.Current.StreakShieldsRemaining);
            now = now.AddSeconds(121);
            checkGap();
            Assert.False(engine.Progress.PendingStreakBreak);
            Assert.Equal(9, engine.Progress.ConsecutiveDays);
            Assert.Equal(0, settings.Current.StreakShieldsRemaining);
            Assert.Equal(350, awarded);
            sync.LoginStreakReceived!("streak-test", cloud);
            engine.Reset(); // logout replaced the progress object before the posted reply ran
            checkGap();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, engine.Progress.ConsecutiveDays);
            sync.Reset();
            settings.Current.UnifiedId = "next-account";
            Assert.Equal(now.ToLocalTime().Date, engine.Progress.LastLaunchDate);
            Assert.Equal(1, sync.LoginStreak!()!.Value.Streak);
            engine.Progress.ConsecutiveDays = 2;
            engine.Progress.LastLaunchDate = now.ToLocalTime().Date.AddDays(-1);
            var beforeStartupBonus = awarded;
            var resumed = App.StartSeasonActivity(settings, engine, () => now);
            Assert.Equal(3, engine.Progress.ConsecutiveDays);
            Assert.Equal(beforeStartupBonus, awarded);
            resumed();
            Assert.Equal(beforeStartupBonus + 50, awarded);
        }
        finally
        {
            CoreProgression.AddXPProvider = previousXp;
            settings.SaveImmediate();
            settings.SealForReset();
            foreach (var file in Directory.GetFiles(CorePaths.UserData, Path.GetFileName(achievementPath) + "*")) File.Delete(file);
        }
    }
}
