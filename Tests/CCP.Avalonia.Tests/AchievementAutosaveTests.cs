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
    public Task ExitFlushesProgressWhileAutosaveIsPendingAndDirtyIsClear() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-ach-exit-").FullName;
        var path = Path.Combine(dir, "achievements.json");
        var store = new AchievementStore(path);
        var engine = new AchievementEngine(store);
        engine.TrackPinkFilterMinutes(2);
        engine.TrackSpiralMinutes(3);
        var timer = AvApp.StartAchievementAutosave(engine);
        Task? pending = null;
        try
        {
            lock (store._saveLock)
            {
                pending = engine.SaveIfDirtyAsync(); // queued writer cannot take this lock yet
                Assert.False(engine.IsDirty);
                Assert.False(pending.IsCompleted);
                AvApp.SaveAchievementsOnExit(timer, engine); // synchronous, re-enters the lock on this thread
                Assert.False(timer.IsEnabled);
                Assert.False(pending.IsCompleted);
                var saved = new AchievementStore(path).Load(); // exit must have written before the task can
                Assert.Equal(2, saved.TotalPinkFilterMinutes);
                Assert.Equal(3, saved.TotalSpiralMinutes);
            }
        }
        finally
        {
            timer.Stop();
            if (pending != null) await pending; // drain before deleting the sandbox, even after a failed assertion
            Directory.Delete(dir, true);
        }
    });

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
