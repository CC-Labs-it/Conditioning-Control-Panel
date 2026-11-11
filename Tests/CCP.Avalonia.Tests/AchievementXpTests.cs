using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class AchievementXpTests
{
    // The splash yields between account startup and the later achievement hooks. The ordering guard
    // covers the real startup method; the claim exercises the same early event and saved progress.
    [Fact]
    public void RestoredSessionClaimIsCountedBeforeLaterStartupSteps()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(TestDirectory(), "../../CCP.Avalonia/App.axaml.cs")));
        var startup = source.IndexOf("private async Task StartDesktop(", StringComparison.Ordinal);
        var create = source.IndexOf("Achievements = new AchievementEngine(", startup, StringComparison.Ordinal);
        var wire = source.IndexOf("SeedAchievementXp(Achievements);", startup, StringComparison.Ordinal);
        var account = source.IndexOf("if (Platform.AccountSeed.Seed())", startup, StringComparison.Ordinal);
        Assert.True(startup >= 0 && create > startup && wire > create && account > wire);

        var old = (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreAccount.IsLoggedInProvider);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = System.Threading.Timeout.Infinite };
        var queue = new Queue<Action>();
        var dir = Directory.CreateTempSubdirectory("ccp-early-xp-").FullName;
        var path = Path.Combine(dir, "achievements.json");
        var engine = new AchievementEngine(new AchievementStore(path));
        var hook = App.SeedAchievementXp(engine);
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreDispatch.PostProvider = queue.Enqueue;
            CoreAccount.IsLoggedInProvider = () => true;
            var s = service.Current;
            s.UnifiedId = "early";
            s.OfflineMode = false;
            s.LastWebXpClaimId = null;
            var sync = new SyncPush(() => null, () => false);
            sync.MarkLoaded(null);
            var claim = JObject.Parse("""{"web_xp":{"claim":{"id":"early-claim","amount":7}}}""");
            sync.ClaimWebXp(s, claim);
            while (queue.TryDequeue(out var pay)) pay();
            Assert.Equal("early-claim", s.LastWebXpClaimId);
            Assert.Equal(7, engine.Progress.TotalXPEarned);
            engine.Save();
            Assert.Equal(7, new AchievementStore(path).Load().TotalXPEarned);
        }
        finally
        {
            ProgressionBank.XPAwarded -= hook;
            service.SealForReset();
            (CoreSettings.ServiceProvider, CoreDispatch.PostProvider, CoreAccount.IsLoggedInProvider) = old;
            Directory.Delete(dir, true);
        }
    }

    private static string TestDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

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
