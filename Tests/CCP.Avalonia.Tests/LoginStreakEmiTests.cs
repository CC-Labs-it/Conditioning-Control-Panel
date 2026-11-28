using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF AchievementProgressStreak.cs:67, :118, :137, :147. Swaps the shared EMI seam and settings provider.
[Collection(RunsAloneCollection.Name)]
public class LoginStreakEmiTests
{
    private static readonly DateTime Day = new(2026, 4, 7);

    private static List<string> Run(Action<AchievementEngine, AppSettings> act, int daysAgo, bool offline,
        string? skill = null)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-streak-emi-").FullName;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldSeam = LoginStreakHead.EmiStreakMoment;
        var fired = new List<string>();
        var service = new SettingsService(Path.Combine(dir, "settings.json"));
        var s = service.Current;
        s.OfflineMode = offline;
        s.OfflineUsername = "offline-test";
        if (!offline) s.UnifiedId = "cloud-id";
        s.StreakShieldsRemaining = 1;
        s.StreakFixCharges = 1;
        if (skill != null) s.UnlockedSkills.Add(skill);
        CoreSettings.ServiceProvider = () => service;
        LoginStreakHead.EmiStreakMoment = (moment, streak) => fired.Add($"{moment}:{streak}");
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            engine.Progress.LastLaunchDate = Day.AddDays(-daysAgo);
            engine.Progress.ConsecutiveDays = 5;
            act(engine, s);
            return fired;
        }
        finally
        {
            LoginStreakHead.EmiStreakMoment = oldSeam;
            LoginStreakHead.Stop();
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RolloverKeepFiresOnceWithTheNewStreak()
    {
        var fired = Run((e, s) =>
        {
            Assert.True(LoginStreakHead.Rollover(e, s, Day));
            Assert.False(LoginStreakHead.Rollover(e, s, Day));
        }, 1, true);
        Assert.Equal(new[] { "streakKept:6" }, fired);
    }

    [Fact]
    public void StartupBankStaysSilentAsWpfHasNoDeskYet()
    {
        // WPF banks in the AchievementService ctor (App.xaml.cs:2849) before App.EmiDesk exists (:2859).
        var fired = Run((e, s) => Assert.True(LoginStreakHead.Update(e.Progress, s, Day, false)), 1, true);
        Assert.Empty(fired);
    }

    [Theory]
    [InlineData("good_girl_streak", "streakKept:6")]
    [InlineData("oopsie_insurance", "streakKept:5")]
    [InlineData(null, "streakBroken:5")]
    public void RolloverGapFiresKeepOrBreak(string? skill, string expected)
    {
        var fired = Run((e, s) => Assert.True(LoginStreakHead.Rollover(e, s, Day)), 3, true, skill);
        Assert.Equal(new[] { expected }, fired);
    }

    [Fact]
    public void DeferredBreakFiresOnlyWhenTheCloudConfirmsTheGap()
    {
        var yesterday = Day.AddDays(-1).ToString("yyyy-MM-dd");
        var covered = Run((e, s) =>
        {
            Assert.False(LoginStreakHead.Update(e.Progress, s, Day, false));
            LoginStreakHead.ApplyCloud(e, s, JObject.Parse($"{{\"consecutive_days\":5,\"last_streak_date\":\"{yesterday}\"}}"), Day);
            Assert.Equal(6, e.Progress.ConsecutiveDays);
        }, 3, false);
        Assert.Empty(covered);   // WPF ResolveDeferredStreakBreak :240 has no moment
        var broken = Run((e, s) =>
        {
            Assert.False(LoginStreakHead.Update(e.Progress, s, Day, false));
            LoginStreakHead.ApplyCloud(e, s, null, Day);
            Assert.Equal(1, e.Progress.ConsecutiveDays);
        }, 3, false);
        Assert.Equal(new[] { "streakBroken:5" }, broken);
    }
}
