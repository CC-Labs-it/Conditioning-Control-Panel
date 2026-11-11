using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LoginStreakRolloverTests
{
    [Fact]
    public void RolloverBanksOnceAndPaysAndPersists()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rollover-").FullName;
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
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
            LoginStreakHead.ClearProfile();
            ProgressionBank.Awarded -= Awarded;
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    }

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

    [Fact]
    public void SeasonMarksUtcDayOnceAndPeakOnlyRises()
    {
        var s = new AppSettings();
        var utc = new DateTime(2026, 4, 7, 23, 0, 0, DateTimeKind.Utc);
        LoginStreakHead.MarkSeason(s, 5, utc);
        LoginStreakHead.MarkSeason(s, 2, utc);
        Assert.Single(s.SeasonActiveDays);
        Assert.Equal("2026-04-07", s.SeasonActiveDays[0]);
        Assert.Equal(5, s.SeasonPeakStreak);
        LoginStreakHead.MarkSeason(s, 7, utc.AddDays(1));
        Assert.Equal(2, s.SeasonActiveDays.Count);
        Assert.Equal(7, s.SeasonPeakStreak);
    }
}
