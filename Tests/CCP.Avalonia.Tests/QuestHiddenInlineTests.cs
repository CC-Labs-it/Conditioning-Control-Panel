using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Avalonia #22339 (12.1.3+): a TextBlock hidden on its own IsVisible while it holds an
/// InlineUIContainer throws on its first render. The quest bonus lines hide that way, so hiding must
/// drop the inline icons.</summary>
public sealed class QuestHiddenInlineTests
{
    private static void Setup()
    {
        if (Application.Current is not null) return;
        var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
        var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
        mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
        fonts.FontFamilyMappings = mappings;
        global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static bool HoldsInlineControl(TextBlock t) => t.Inlines?.OfType<InlineUIContainer>().Any() == true;

    [Fact]
    public void InitiallyEmptySeatShowsAndRenders() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var card = new DailyQuestCard();   // the constructor seeds a streak bonus with its Fire icon
        var bonus = card.FindControl<TextBlock>("TxtXpBonus")!;
        Assert.True(HoldsInlineControl(bonus));
        card.ShowEmpty(0, "empty", "none");
        Assert.False(bonus.IsVisible);
        Assert.False(HoldsInlineControl(bonus));
        var w = new Window { Content = card, Width = 600, Height = 300 };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(w.CaptureRenderedFrame());
        }
        finally { w.Close(); }
    });

    [Fact]
    public void RotatedAwayWeeklyDropsBonusIconsAndRenders() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-quests-inline-").FullName;
        var oldQuests = AvApp.Quests;
        var oldProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        Window? w = null;
        try
        {
            settings.Current.DailyQuestStreak = 3;   // a streak bonus with its Fire icon on the weekly
            var quests = new QuestService(null, dir);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            AvApp.Quests = quests;

            var tab = new QuestsTabView();
            tab.RefreshQuestUI();   // painted before it was ever measured
            var streak = tab.FindControl<TextBlock>("TxtWeeklyStreakBonus")!;
            var reroll = tab.FindControl<TextBlock>("TxtWeeklyRerollBonus")!;
            Assert.True(streak.IsVisible);
            Assert.True(HoldsInlineControl(streak));

            // The finished weekly's definition rotated away (#496): the "done for the week" card.
            quests.Progress.WeeklyQuest = new ActiveQuest("rotated_away_w") { IsCompleted = true };
            tab.RefreshQuestUI();
            Assert.False(streak.IsVisible);
            Assert.False(reroll.IsVisible);
            Assert.False(HoldsInlineControl(streak));
            Assert.False(HoldsInlineControl(reroll));

            w = new Window { Content = tab, Width = 1400, Height = 1000 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(w.CaptureRenderedFrame());
        }
        finally
        {
            w?.Close();
            AvApp.Quests = oldQuests;
            CoreSettings.ServiceProvider = oldProvider;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    });
}
