using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using ConditioningControlPanel.Avalonia.Platform;
using Newtonsoft.Json.Linq;

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
    public void YesterdayIsBankedPersistedAndCreditedOnce()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-login-start-").FullName;
        try
        {
            var today = new DateTime(2026, 4, 7);
            var path = Path.Combine(dir, "achievements.json");
            var original = new AchievementEngine(new AchievementStore(path));
            original.Progress.LastLaunchDate = today.AddDays(-1);
            original.Progress.ConsecutiveDays = 4;
            original.Save();
            var engine = new AchievementEngine(new AchievementStore(path));
            var settings = new AppSettings { OfflineMode = true };
            Assert.True(LoginStreakHead.Update(engine.Progress, settings, today, false));
            Assert.Equal(5, engine.Progress.ConsecutiveDays);
            Assert.Equal(today, settings.LastStreakDate);
            Assert.True(engine.Progress.PendingStreakBonus);
            engine.Save();
            var reloaded = new AchievementEngine(new AchievementStore(path));
            Assert.Equal(5, reloaded.Progress.ConsecutiveDays);
            Assert.False(LoginStreakHead.Update(reloaded.Progress, settings, today, false));
            using var quests = new QuestService(null, dir);
            quests.Progress.WeeklyQuest = new ActiveQuest("streak_keeper_w");
            AvApp.CreditBankedLoginStreak(quests, reloaded.Progress, today);
            Assert.Equal(5, quests.Progress.WeeklyQuest.CurrentProgress);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StartupPaysYesterdayBonusOnceAndPersistsBeforeRestart()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-login-startup-").FullName;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldQuests = AvApp.Quests;
        var settingsService = new SettingsService();
        var s = settingsService.Current;
        s.OfflineMode = true;
        s.OfflineUsername = "offline-test";
        CoreSettings.ServiceProvider = () => settingsService;
        var awards = 0;
        void Awarded(double amount, string source) { if (source == "Other" && amount == 100) awards++; }
        ProgressionBank.Awarded += Awarded;
        try
        {
            var path = Path.Combine(dir, "achievements.json");
            var engine = new AchievementEngine(new AchievementStore(path));
            engine.Progress.LastLaunchDate = DateTime.Today.AddDays(-1);
            engine.Progress.ConsecutiveDays = 4;
            engine.Save();
            var started = new AchievementEngine(new AchievementStore(path));
            LoginStreakHead.Start(started, s);
            var restarted = new AchievementEngine(new AchievementStore(path));
            Assert.Equal(5, restarted.Progress.ConsecutiveDays);
            Assert.Equal(DateTime.Today, restarted.Progress.LastLaunchDate);
            Assert.Equal(5, s.CurrentStreak);
            Assert.Equal(0, awards);
            Assert.True(started.Progress.PendingStreakBonus);
            using var quests = new QuestService(null, dir);
            quests.Progress.WeeklyQuest = new ActiveQuest("conditioning_champion_w");
            AvApp.Quests = quests;
            AvApp.WireQuestXpAndPay(started);
            Assert.Equal(1, awards);
            Assert.Equal(100, quests.Progress.WeeklyQuest.CurrentProgress);
            Assert.False(started.Progress.PendingStreakBonus);
            quests.Progress.WeeklyQuest = new ActiveQuest("streak_keeper_w");
            AvApp.CreditBankedLoginStreak(quests, restarted.Progress, DateTime.Today);
            Assert.Equal(5, quests.Progress.WeeklyQuest.CurrentProgress);
            LoginStreakHead.Start(restarted, s);
            Assert.Equal(1, awards);
        }
        finally
        {
            ProgressionBank.Awarded -= Awarded;
            ProgressionBank.QuestAwarded -= AvApp.TrackQuestXp;
            AvApp.Quests = oldQuests;
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(true, true, 6, 0, 1)]
    [InlineData(false, true, 5, 0, 0)]
    [InlineData(false, false, 1, 0, 1)]
    public void OfflineGapUsesShieldThenInsurance(bool shield, bool insurance, int expected, int shieldsLeft, int chargesLeft)
    {
        var today = new DateTime(2026, 4, 7);
        var s = new AppSettings { OfflineMode = true, StreakShieldsRemaining = shield ? 1 : 0, StreakFixCharges = 1 };
        if (shield) s.UnlockedSkills.Add("good_girl_streak");
        if (insurance) s.UnlockedSkills.Add("oopsie_insurance");
        var p = new AchievementProgress { LastLaunchDate = today.AddDays(-3), ConsecutiveDays = 5 };
        Assert.True(LoginStreakHead.Update(p, s, today, false));
        Assert.Equal(expected, p.ConsecutiveDays);
        Assert.Equal(shieldsLeft, s.StreakShieldsRemaining);
        Assert.Equal(chargesLeft, s.StreakFixCharges);
        Assert.Equal(shield ? 2 : 0, s.StreakShieldUsedDates.Count);
        Assert.Equal(shield, p.PendingStreakBonus);
    }

    [Fact]
    public void CloudGapWaitsAndAdoptsPhoneDayWithoutSpendingShield()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-login-cloud-").FullName;
        try
        {
            var today = new DateTime(2026, 4, 7);
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            var p = engine.Progress;
            p.LastLaunchDate = today.AddDays(-3); p.ConsecutiveDays = 5;
            var s = new AppSettings { UnifiedId = "cloud-id", StreakShieldsRemaining = 1 };
            s.UnlockedSkills.Add("good_girl_streak");
            Assert.False(LoginStreakHead.Update(p, s, today, false));
            Assert.True(p.PendingStreakBreak);
            Assert.Equal(today.AddDays(-3), p.LastLaunchDate);
            LoginStreakHead.ApplyCloud(engine, s, JObject.Parse("{\"consecutive_days\":7,\"last_streak_date\":\"2026-04-07\"}"), today);
            Assert.False(p.PendingStreakBreak);
            Assert.Equal(7, p.ConsecutiveDays);
            Assert.Equal(today, p.LastLaunchDate);
            Assert.Equal(1, s.StreakShieldsRemaining);
            Assert.False(p.PendingStreakBonus);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnansweredGapResolvesOnceWithShield()
    {
        var today = new DateTime(2026, 4, 7);
        var s = new AppSettings { UnifiedId = "cloud-id", StreakShieldsRemaining = 1 };
        s.UnlockedSkills.Add("good_girl_streak");
        var p = new AchievementProgress { LastLaunchDate = today.AddDays(-3), ConsecutiveDays = 5 };
        Assert.False(LoginStreakHead.Update(p, s, today, false));
        Assert.True(LoginStreakHead.Update(p, s, today, true));
        Assert.Equal(6, p.ConsecutiveDays);
        Assert.False(p.PendingStreakBreak);
        Assert.Equal(0, s.StreakShieldsRemaining);
        Assert.False(LoginStreakHead.Update(p, s, today, true));
        Assert.Equal(6, p.ConsecutiveDays);
    }

    [Fact]
    public void FirstLaunchStartsAtOneWithoutBonus()
    {
        var today = new DateTime(2026, 4, 7);
        var p = new AchievementProgress();
        var s = new AppSettings { OfflineMode = true };
        Assert.True(LoginStreakHead.Update(p, s, today, false));
        Assert.Equal(1, p.ConsecutiveDays);
        Assert.Equal(today, p.LastLaunchDate);
        Assert.False(p.PendingStreakBonus);
    }

    [Theory]
    [InlineData("2026-04-07", true)]
    [InlineData("2026-04-07T04:05:06Z", true)]
    [InlineData("2026-04-07T04:05:06.1234567+01:00", true)]
    [InlineData("2026-04-09", false)]
    [InlineData("not-a-day", false)]
    public void CloudDayKeysFollowWpfParsing(string value, bool expected)
    {
        Assert.Equal(expected, LoginStreakHead.TryParseCloudDay(value, new DateTime(2026, 4, 7), out var parsed));
        if (expected) Assert.Equal(new DateTime(2026, 4, 7), parsed);
    }

    [Fact]
    public void V2ProfileReadsStreakStats()
    {
        var user = JObject.Parse("{\"stats\":{\"consecutive_days\":7,\"last_streak_date\":\"2026-04-07\"}}").ToObject<V2AuthService.V2User>();
        Assert.Equal(7, (int)user!.Stats!["consecutive_days"]!);
        Assert.Equal("2026-04-07", (string?)user.Stats["last_streak_date"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedCloudReplyCannotChangeReplacedProgress(bool signOut) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-login-stale-").FullName;
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldEngine = property.GetValue(null);
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        service.Current.UnifiedId = "cloud-id";
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            property.SetValue(null, engine);
            engine.Progress.ConsecutiveDays = 2;
            engine.Progress.LastLaunchDate = DateTime.Today.AddDays(-3);
            engine.Progress.PendingStreakBreak = true;
            LoginStreakHead.ProfileLoaded("cloud-id", JObject.Parse("{\"consecutive_days\":20}"));
            if (signOut) LoginStreakHead.ClearProfile();
            else
            {
                engine.Reset();
                engine.Progress.ConsecutiveDays = 2;
                engine.Progress.LastLaunchDate = DateTime.Today.AddDays(-3);
                engine.Progress.PendingStreakBreak = true;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, engine.Progress.ConsecutiveDays);
            Assert.True(engine.Progress.PendingStreakBreak);
        }
        finally
        {
            LoginStreakHead.ClearProfile();
            property.SetValue(null, oldEngine);
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimeoutResolvesOnlyTheCurrentProfile(bool replace) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-login-timeout-").FullName;
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldEngine = property.GetValue(null);
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var settings = service.Current;
        settings.UnifiedId = "cloud-id";
        settings.OfflineMode = false;
        settings.StreakShieldsRemaining = 1;
        settings.UnlockedSkills.Add("good_girl_streak");
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            property.SetValue(null, engine);
            engine.Progress.LastLaunchDate = DateTime.Today.AddDays(-3);
            engine.Progress.ConsecutiveDays = 5;
            engine.Progress.PendingStreakBreak = true;
            _ = LoginStreakHead.Timeout(engine, settings, "cloud-id", Task.CompletedTask);
            if (replace)
            {
                engine.Reset();
                engine.Progress.LastLaunchDate = DateTime.Today.AddDays(-3);
                engine.Progress.ConsecutiveDays = 5;
                engine.Progress.PendingStreakBreak = true;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(replace ? 5 : 6, engine.Progress.ConsecutiveDays);
            Assert.Equal(replace ? 1 : 0, settings.StreakShieldsRemaining);
            Assert.Equal(replace, engine.Progress.PendingStreakBreak);
        }
        finally
        {
            LoginStreakHead.ClearProfile();
            property.SetValue(null, oldEngine);
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CloudGapBonusReachesXpQuestWhetherItResolvesBeforeOrAfterWiring(bool before)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-login-xp-").FullName;
        var oldProvider = CoreSettings.ServiceProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldQuests = AvApp.Quests;
        var settingsService = new SettingsService();
        var settings = settingsService.Current;
        settings.UnifiedId = "cloud-id";
        settings.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settingsService;
        CoreAccount.IsLoggedInProvider = () => true;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            engine.Progress.LastLaunchDate = DateTime.Today.AddDays(-3);
            engine.Progress.ConsecutiveDays = 4;
            using var quests = new QuestService(null, dir);
            quests.Progress.WeeklyQuest = new ActiveQuest("conditioning_champion_w");
            AvApp.Quests = quests;
            LoginStreakHead.Start(engine, settings);
            var stats = new JObject { ["consecutive_days"] = 5, ["last_streak_date"] = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
            if (before) LoginStreakHead.ApplyCloud(engine, settings, stats, DateTime.Today);
            Assert.Equal(0, quests.Progress.WeeklyQuest.CurrentProgress);
            AvApp.WireQuestXpAndPay(engine);
            if (!before) LoginStreakHead.ApplyCloud(engine, settings, stats, DateTime.Today);
            Assert.Equal(100, quests.Progress.WeeklyQuest.CurrentProgress);
            Assert.False(engine.Progress.PendingStreakBonus);
        }
        finally
        {
            ProgressionBank.QuestAwarded -= AvApp.TrackQuestXp;
            AvApp.Quests = oldQuests;
            CoreAccount.IsLoggedInProvider = oldLogin;
            CoreSettings.ServiceProvider = oldProvider;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void StartupCreditsAfterQuestServiceIsCreated()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "CCP.Avalonia", "App.axaml.cs"));
        var source = File.ReadAllText(path);
        Assert.Contains("WireQuestXpAndPay(Achievements);\n            CreditBankedLoginStreak(Quests, Achievements?.Progress, DateTime.Today);", source.Replace("\r\n", "\n"));
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
