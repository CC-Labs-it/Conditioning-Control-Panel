using System;
using System.IO;
using System.Threading;
using System.Linq;
using CCP.Avalonia.Testing;
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

    private static void Exercise()
    {
        var settings = new SettingsService();
        settings.Current.SeasonActiveDays.Clear();
        settings.Current.SeasonPeakStreak = 0;
        var achievementPath = Path.Combine(CorePaths.UserData, "season-test-" + Guid.NewGuid().ToString("N") + ".json");
        var engine = new AchievementEngine(new AchievementStore(achievementPath));
        var now = new DateTime(2026, 7, 3, 23, 59, 0, DateTimeKind.Utc);
        try
        {
            var check = App.StartSeasonActivity(settings, engine, () => now);
            Assert.Equal(new[] { "2026-07-03" }, settings.Current.SeasonActiveDays);
            Assert.Equal(1, engine.Progress.ConsecutiveDays);
            Assert.Equal(1, settings.Current.SeasonPeakStreak);
            Assert.Equal(1, new AchievementStore(achievementPath).Load().ConsecutiveDays);
            engine.Progress.ConsecutiveDays = 7;
            check();
            Assert.Equal(7, settings.Current.SeasonPeakStreak);
            var path = Path.Combine(CorePaths.UserData, "settings.json");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && File.ReadAllText(path).Contains("2026-07-03"), TimeSpan.FromSeconds(10)));
            engine.Progress.ConsecutiveDays = 9;
            check();
            Assert.Equal(9, settings.Current.SeasonPeakStreak);
            engine.Progress.ConsecutiveDays = 1;
            now = now.AddMinutes(2);
            check();
            Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && File.ReadAllText(path).Contains("2026-07-04"), TimeSpan.FromSeconds(10)));
            var loaded = new SettingsService();
            Assert.Equal(new[] { "2026-07-03", "2026-07-04" }, loaded.Current.SeasonActiveDays);
            Assert.Equal(9, loaded.Current.SeasonPeakStreak);
        }
        finally
        {
            settings.SaveImmediate();
            settings.SealForReset();
            foreach (var file in Directory.GetFiles(CorePaths.UserData, Path.GetFileName(achievementPath) + "*")) File.Delete(file);
        }
    }
}
