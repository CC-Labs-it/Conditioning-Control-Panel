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

    [Fact]
    public void FailedHeadGrantLeavesMilestoneAvailable()
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.UnlockedSkills.Add("perfect_bimbo_week");
        settings.Current.DailyQuestStreak = 7;
        settings.Current.LastPerfectWeekStreakAwarded = 0;
        CoreProgression.AddXPProvider = (_, _) => throw new InvalidOperationException("Failed grant");
        AvApp.WireQuestRewards(null);
        Assert.Throws<InvalidOperationException>(() => CoreQuests.CheckPerfectWeekBonusProvider!());
        Assert.Equal(0, settings.Current.LastPerfectWeekStreakAwarded);
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
