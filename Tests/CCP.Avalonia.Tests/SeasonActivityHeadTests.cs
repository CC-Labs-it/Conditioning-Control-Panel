using System;
using System.IO;
using System.Threading;
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
        var settings = new SettingsService();
        settings.Current.SeasonActiveDays.Clear();
        settings.Current.SeasonPeakStreak = 0;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(CorePaths.UserData, "season-test-achievements.json")));
        engine.Progress.ConsecutiveDays = 7;
        var now = new DateTime(2026, 7, 3, 23, 59, 0, DateTimeKind.Utc);
        try
        {
            var check = App.StartSeasonActivity(settings, engine, () => now);
            Assert.Equal(new[] { "2026-07-03" }, settings.Current.SeasonActiveDays);
            Assert.Equal(7, settings.Current.SeasonPeakStreak);
            var path = Path.Combine(CorePaths.UserData, "settings.json");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && File.ReadAllText(path).Contains("2026-07-03"), TimeSpan.FromSeconds(10)));
            engine.Progress.ConsecutiveDays = 1;
            now = now.AddMinutes(2);
            check();
            Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && File.ReadAllText(path).Contains("2026-07-04"), TimeSpan.FromSeconds(10)));
            var loaded = new SettingsService();
            Assert.Equal(new[] { "2026-07-03", "2026-07-04" }, loaded.Current.SeasonActiveDays);
            Assert.Equal(7, loaded.Current.SeasonPeakStreak);
        }
        finally { settings.SaveImmediate(); }
    });
}
