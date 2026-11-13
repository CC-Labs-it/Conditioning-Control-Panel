using System;
using System.IO;
using System.Linq;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

// Restack b57: the head half of #4696's season writers (App.NoteSeasonActivity on startup and each autosave tick).
public class SeasonActivityHeadTests
{
    [Fact]
    public void StartupAndRolloverRecordDaysAndPeak() => AvaloniaTestDispatcher.Run(() =>
    {
        var root = CorePaths.UserData;
        var original = Directory.GetFiles(root, "settings*").ToDictionary(Path.GetFileName, File.ReadAllBytes);
        try
        {
            var settings = new SettingsService();
            settings.Current.SeasonActiveDays.Clear();
            settings.Current.SeasonPeakStreak = 0;
            var progress = new AchievementProgress { ConsecutiveDays = 3 };
            var now = new DateTime(2026, 7, 3, 23, 59, 0, DateTimeKind.Utc);
            App.NoteSeasonActivity(settings, progress, now);
            Assert.Equal(new[] { "2026-07-03" }, settings.Current.SeasonActiveDays);
            Assert.Equal(3, settings.Current.SeasonPeakStreak);
            progress.ConsecutiveDays = 1;
            App.NoteSeasonActivity(settings, progress, now.AddMinutes(2));
            Assert.Equal(new[] { "2026-07-03", "2026-07-04" }, settings.Current.SeasonActiveDays);
            Assert.Equal(3, settings.Current.SeasonPeakStreak);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(root, "settings*")) File.Delete(file);
            foreach (var item in original) File.WriteAllBytes(Path.Combine(root, item.Key!), item.Value);
        }
    });
}
