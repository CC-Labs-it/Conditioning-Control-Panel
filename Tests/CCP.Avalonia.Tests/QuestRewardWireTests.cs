using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class QuestRewardWireTests
{
    [Fact]
    public void BonusPopupHasWpfCopyAndPanicClosesIt() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        if (global::Avalonia.Application.Current is null)
            AvAppBuilder();
        var first = AvApp.ShowPerfectWeekBonus(3060, 7);
        var popup = AvApp.ShowPerfectWeekBonus(3060, 7);
        try
        {
            // WPF stacks bonus popups: a second one does not close the first.
            Assert.True(first.IsVisible);
            Assert.True(popup.IsVisible);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("skill_perfect_week_name_popup"),
                popup.FindControl<global::Avalonia.Controls.TextBlock>("TxtName")!.Text);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("skill_perfect_week_flavor_popup", 7, 3060),
                popup.FindControl<global::Avalonia.Controls.TextBlock>("TxtFlavor")!.Text);
            var surface = Assert.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All,
                s => s.Id == "perfect-week-bonus");
            surface.Stop(null);
            Assert.False(first.IsVisible);
            Assert.False(popup.IsVisible);
        }
        finally { first.Close(); popup.Close(); }
    });

    private static void AvAppBuilder() => ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
        .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();

    [Fact]
    public void HeadPaysOnceAndPanicCancelsTheQueuedNotification() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null) AvAppBuilder();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.UnlockedSkills.Add("perfect_bimbo_week");
        settings.Current.DailyQuestStreak = 7;
        settings.Current.PlayerLevel = 1;
        settings.Current.LastPerfectWeekStreakAwarded = 0;
        int paid = 0;
        CoreProgression.AddXPProvider = (xp, source) => { Assert.Equal("Other", source); paid += (int)xp; };
        AvApp.WireQuestRewards(null);
        Assert.Equal(3060, CoreQuests.CheckPerfectWeekBonusProvider!());
        Assert.Equal(3060, paid);
        Assert.Equal(7, settings.Current.LastPerfectWeekStreakAwarded);
        Assert.Equal(0, CoreQuests.CheckPerfectWeekBonusProvider!());
        var surface = Assert.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All,
            s => s.Id == "perfect-week-bonus");
        surface.Stop(null);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var popups = typeof(AvApp).GetField("_perfectWeekPopups", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Empty((System.Collections.IEnumerable)popups.GetValue(null)!);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedHeadGrantLeavesMilestoneAvailable(bool missingProvider)
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.UnlockedSkills.Add("perfect_bimbo_week");
        settings.Current.DailyQuestStreak = 7;
        settings.Current.LastPerfectWeekStreakAwarded = 0;
        CoreProgression.AddXPProvider = missingProvider ? null : (_, _) => throw new InvalidOperationException("Failed grant");
        AvApp.WireQuestRewards(null);
        Assert.Equal(0, CoreQuests.CheckPerfectWeekBonusProvider!());
        Assert.Equal(0, settings.Current.LastPerfectWeekStreakAwarded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedBonusDoesNotInterruptCompletionOrTheRemainingBoard(bool missingProvider)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-quest-bonus-failure-").FullName;
        try
        {
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            using var quests = new QuestService(null, dir);
            settings.Current.UnlockedSkills.Add("perfect_bimbo_week");
            settings.Current.DailyQuestStreak = 7;
            settings.Current.LastPerfectWeekStreakAwarded = 0;
            quests.Progress.DailyQuestCompletionDates.Clear();
            for (int day = 0; day < 7; day++)
                quests.Progress.DailyQuestCompletionDates.Add(DateTime.Today.AddDays(-day));
            // Two matching seats prove a failed bonus cannot unwind the per-seat pass.
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("pop_parade_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("bubble_storm_w");
            CoreProgression.AddXPProvider = missingProvider ? null : (_, source) =>
            {
                if (source == "Other") throw new InvalidOperationException("Failed bonus grant");
            };
            int completed = 0, effects = 0;
            quests.QuestCompleted += (_, _) => completed++;
            CoreQuests.PlayCompletionEffectsProvider = () => effects++;
            AvApp.WireQuestRewards(null);

            quests.TrackBubblesPopped(40);

            Assert.All(quests.Progress.DailyQuests, quest => Assert.True(quest.IsCompleted));
            Assert.Equal(2, completed);
            Assert.Equal(2, effects);
            Assert.Equal(40, quests.Progress.WeeklyQuest.CurrentProgress);
            Assert.Equal(0, settings.Current.LastPerfectWeekStreakAwarded);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OnlyCompletedSessionsAdvanceSessionQuestsAndWiringSeedsBonus()
    {
        var oldQuests = AvApp.Quests;
        var oldBonus = CoreQuests.CheckPerfectWeekBonusProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-session-quests-").FullName;
        using var quests = new QuestService(null, dir);
        var runner = new SessionRunner(new SessionLogService());
        try
        {
            AvApp.Quests = quests;
            // Use the built-in Sessions definition rather than inventing a test-only pool.
            var def = QuestDefinition.DailyQuests.Find(d => d.Category == QuestCategory.Session)!;
            Assert.NotNull(def);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new(def.Id) };
            CoreQuests.CheckPerfectWeekBonusProvider = null;
            AvApp.WireQuestRewards(runner);
            Assert.NotNull(CoreQuests.CheckPerfectWeekBonusProvider);
            var session = new Session { Id = "quest-credit", Name = "Quest credit", DurationMinutes = 1 };
            runner.Start(session);
            runner.Stop();
            Assert.Equal(0, quests.Progress.DailyQuests[0].CurrentProgress);
            runner.Start(session);
            runner.Stop(completed: true);
            Assert.Equal(1, quests.Progress.DailyQuests[0].CurrentProgress);
            runner.Stop(completed: true);
            Assert.Equal(1, quests.Progress.DailyQuests[0].CurrentProgress);
        }
        finally
        {
            runner.Stop();
            AvApp.Quests = oldQuests;
            CoreQuests.CheckPerfectWeekBonusProvider = oldBonus;
            Directory.Delete(dir, true);
        }
    }
}
