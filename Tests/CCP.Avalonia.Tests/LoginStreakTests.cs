using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class LoginStreakTests
{
    [Theory]
    [InlineData(0, false, 4)]
    [InlineData(-1, false, 0)]
    [InlineData(1, false, 0)]
    [InlineData(0, true, 0)]
    public void OnlyBankedCurrentDayCreditsWeeklyQuest(int dayOffset, bool pending, int expected)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-login-streak-").FullName;
        try
        {
            using var quests = new QuestService(null, dir);
            quests.Progress.WeeklyQuest = new ActiveQuest("streak_keeper_w");
            var today = new DateTime(2026, 4, 7);
            var progress = new AchievementProgress { LastLaunchDate = today.AddDays(dayOffset), ConsecutiveDays = 4, PendingStreakBreak = pending };
            AvApp.CreditBankedLoginStreak(quests, progress, today);
            Assert.Equal(expected, quests.Progress.WeeklyQuest.CurrentProgress);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StartupCreditsAfterQuestServiceIsCreated()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "CCP.Avalonia", "App.axaml.cs"));
        var source = File.ReadAllText(path);
        Assert.Contains("ProgressionBank.Awarded += (amount, _) => Quests?.TrackXPEarned((int)amount);\n            CreditBankedLoginStreak(Quests, Achievements?.Progress, DateTime.Today);", source.Replace("\r\n", "\n"));
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
