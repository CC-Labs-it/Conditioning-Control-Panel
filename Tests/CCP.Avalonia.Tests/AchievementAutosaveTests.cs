using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class AchievementAutosaveTests
{
    [Fact]
    public Task TimerSavesDirtyOverlayTotalsWithoutAnUnlock() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-ach-autosave-").FullName;
        var path = Path.Combine(dir, "achievements.json");
        var engine = new AchievementEngine(new AchievementStore(path));
        engine.TrackPinkFilterMinutes(2);
        engine.TrackSpiralMinutes(3);
        var timer = AvApp.StartAchievementAutosave(engine);
        try
        {
            Assert.Equal(TimeSpan.FromSeconds(30), timer.Interval);
            Assert.True(timer.IsEnabled);
            timer.Interval = TimeSpan.FromMilliseconds(1); // drive the real dispatcher callback promptly
            var deadline = Stopwatch.StartNew();
            while (new AchievementStore(path).Load().TotalSpiralMinutes != 3)
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), "achievement autosave never wrote dirty totals");
                await Task.Delay(10);
            }
            var saved = new AchievementStore(path).Load();
            Assert.Equal(2, saved.TotalPinkFilterMinutes);
            Assert.Equal(3, saved.ContinuousSpiralMinutes);
            Assert.Empty(saved.UnlockedAchievements);
            Assert.False(engine.IsDirty);
        }
        finally
        {
            timer.Stop();
            Directory.Delete(dir, true);
        }
    });
}
