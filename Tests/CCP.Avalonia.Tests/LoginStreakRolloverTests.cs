using System;
using System.IO;
using System.Reflection;
using CCP.Avalonia.Testing;
using AvApp = ConditioningControlPanel.Avalonia.App;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

// Swaps App.Achievements, the settings provider and the shared rollover timer: runs on its own.
[Collection(RunsAloneCollection.Name)]
public class LoginStreakRolloverTests
{
    [Fact]
    public void RolloverBanksOnceAndPaysAndPersists()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rollover-").FullName;
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService(Path.Combine(dir, "settings.json"));
        service.Current.OfflineMode = true;
        service.Current.OfflineUsername = "offline-test";
        CoreSettings.ServiceProvider = () => service;
        var awards = 0;
        void Awarded(double amount, string source) { if (source == "Other" && amount == 100) awards++; }
        ProgressionBank.Awarded += Awarded;
        try
        {
            var day = new DateTime(2026, 4, 7);
            var path = Path.Combine(dir, "achievements.json");
            var engine = new AchievementEngine(new AchievementStore(path));
            engine.Progress.LastLaunchDate = day.AddDays(-1);
            engine.Progress.ConsecutiveDays = 4;
            LoginStreakHead.QuestXpReady(engine);
            Assert.True(LoginStreakHead.Rollover(engine, service.Current, day));
            Assert.Equal(5, engine.Progress.ConsecutiveDays);
            Assert.Equal(day, engine.Progress.LastLaunchDate);
            Assert.Equal(day, service.Current.LastStreakDate);
            Assert.True(File.Exists(Path.Combine(dir, "settings.json")));
            var loadedSettings = new SettingsService(Path.Combine(dir, "settings.json"));
            Assert.Equal(day, loadedSettings.Current.LastStreakDate);
            Assert.Equal(5, loadedSettings.Current.CurrentStreak);
            Assert.Equal(1, awards);
            Assert.False(LoginStreakHead.Rollover(engine, service.Current, day));
            Assert.False(LoginStreakHead.Rollover(engine, service.Current, day.AddDays(-1)));
            var restarted = new AchievementEngine(new AchievementStore(path));
            Assert.Equal(day, restarted.Progress.LastLaunchDate);
            Assert.False(restarted.Progress.PendingStreakBonus);
            Assert.False(LoginStreakHead.Rollover(restarted, service.Current, day));
            Assert.Equal(1, awards);
        }
        finally
        {
            LoginStreakHead.Stop();
            ProgressionBank.Awarded -= Awarded;
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LogoutKeepsTimerAndBonusForNextProfile() => AvaloniaTestDispatcher.Run(() =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rollover-profile-").FullName;
        var oldProvider = CoreSettings.ServiceProvider;
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldEngine = property.GetValue(null);
        var first = new SettingsService(Path.Combine(dir, "first.json"));
        var next = new SettingsService(Path.Combine(dir, "next.json"));
        first.Current.OfflineMode = next.Current.OfflineMode = true;
        first.Current.OfflineUsername = next.Current.OfflineUsername = "offline-test";
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        var awards = 0;
        void Awarded(double amount, string source) { if (source == "Other" && amount == 100) awards++; }
        ProgressionBank.Awarded += Awarded;
        try
        {
            CoreSettings.ServiceProvider = () => first;
            property.SetValue(null, engine);
            LoginStreakHead.Start(engine, first.Current);
            LoginStreakHead.QuestXpReady(engine);
            first.Current.LastStreakDate = new DateTime(2000, 1, 1);
            LoginStreakHead.ClearProfile();
            engine.Reset();
            CoreSettings.ServiceProvider = () => next;
            var today = new DateTime(2026, 4, 7);
            engine.Progress.LastLaunchDate = today.AddDays(-1);
            engine.Progress.ConsecutiveDays = 4;
            Assert.True(LoginStreakHead.TimerRunning);
            Assert.True(LoginStreakHead.TickCurrent(today));
            Assert.Equal(5, engine.Progress.ConsecutiveDays);
            Assert.Equal(today, next.Current.LastStreakDate);
            Assert.Equal(new DateTime(2000, 1, 1), first.Current.LastStreakDate);
            Assert.Equal(1, awards);
            Assert.False(engine.Progress.PendingStreakBonus);
            Assert.False(LoginStreakHead.TickCurrent(today));
            Assert.Equal(1, awards);
        }
        finally
        {
            LoginStreakHead.Stop();
            ProgressionBank.Awarded -= Awarded;
            property.SetValue(null, oldEngine);
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void PendingGapIsNotOverridden()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rollover-gap-").FullName;
        try
        {
            var day = new DateTime(2026, 4, 7);
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            engine.Progress.LastLaunchDate = day.AddDays(-3);
            engine.Progress.ConsecutiveDays = 5;
            engine.Progress.PendingStreakBreak = true;
            Assert.False(LoginStreakHead.Rollover(engine, new AppSettings(), day));
            Assert.True(engine.Progress.PendingStreakBreak);
            Assert.Equal(5, engine.Progress.ConsecutiveDays);
            Assert.Equal(day.AddDays(-3), engine.Progress.LastLaunchDate);
        }
        finally { Directory.Delete(dir, true); }
    }
}
