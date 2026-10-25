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
