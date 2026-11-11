using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class AchievementXpTests
{
    [Theory]
    [InlineData("award", 30)]
    [InlineData("claim", 7)]
    [InlineData("blocked", 0)]
    public void BankAwardsAndClaimsCountWhileBlockedSourcesDoNot(string kind, double expected)
    {
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var dir = Directory.CreateTempSubdirectory("ccp-xp-").FullName;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        var hook = App.SeedAchievementXp(engine);
        try
        {
            var s = CoreSettings.Current;
            s.OfflineMode = true;
            s.OfflineUsername = "xp-test";
            s.PlayerLevel = 10;
            s.PlayerXP = 0;
            s.DescentMigrationCompleted = false;
            s.UnlockedSkills.Add("pink_rush");
            s.PinkRushActive = true;
            if (kind == "award") ProgressionBank.Add(10, "Quest");
            if (kind == "claim") ProgressionBank.AddClaimed(7);
            if (kind == "blocked") ProgressionBank.Add(10, "Flash");
            Assert.Equal(expected, engine.Progress.TotalXPEarned);
            Assert.Equal(kind != "blocked", engine.IsDirty);
        }
        finally
        {
            ProgressionBank.XPAwarded -= hook;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
            Directory.Delete(dir, true);
        }
    }
}
