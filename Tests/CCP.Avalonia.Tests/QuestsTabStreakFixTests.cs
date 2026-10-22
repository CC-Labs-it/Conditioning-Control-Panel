using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The manual streak fix (WPF MainWindow.QuestsTab.cs:740-963), driven from the button a user clicks.
/// Every missed day needs a day before today in this month, so on the 1st the spend half has nothing to click.</summary>
public sealed class QuestsTabStreakFixTests
{
    [Fact]
    public Task FixDaySpendsOnTheServerAppliesTheBalanceAndAllowsOneSpendAtATime() => RunAsync(async (tab, quests, settings) =>
    {
        settings.UnifiedId = "u1";
        settings.StreakFixCharges = 2;
        var button = tab.FindControl<Button>("BtnFixStreak")!;
        var caption = tab.FindControl<IconLabel>("FixStreakLabel")!;
        var status = tab.FindControl<IconLabel>("TxtFixStreakStatus")!;
        tab.RefreshQuestUI();
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsVisible);
        Assert.Equal("btn_fix_day_with_count", caption.Key);
        Assert.Equal(new object[] { 2 }, caption.Args);
        if (DateTime.Today.Day == 1) { Assert.False(button.IsEnabled); return; }

        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.IsStreakFixMode);
        Assert.Equal("btn_cancel_2", caption.Key);
        Assert.Equal("label_click_a_missed_day_to_fix_it_free", status.Key);
        Assert.Equal(DateTime.Today.Day - 1, tab.FixHighlights.Count);

        var calls = new List<string>();
        var reply = new TaskCompletionSource<(bool, string?, int?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreAccount.UseStreakFixProvider = day => { calls.Add(day); return reply.Task; };
        tab.ConfirmFix = (_, _) => Task.FromResult(true);
        var day1 = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        var first = tab.SpendStreakFixAsync(day1);
        Assert.Equal("label_processing", status.Key);
        // The in-flight guard: a second click returns at once, no second spend off the same balance.
        Assert.True(tab.SpendStreakFixAsync(day1).IsCompleted);
        Assert.Equal(new[] { day1.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, calls);

        reply.SetResult((true, null, 7));
        await first;
        Assert.Equal(7, settings.StreakFixCharges);   // the server's balance, not a local decrement
        Assert.Contains(quests.Progress.DailyQuestCompletionDates, d => d.Date == day1);
        Assert.False(tab.IsStreakFixMode);
        Assert.Equal("label_streak_fixed_success", status.Key);
        Assert.Equal("btn_fix_day_with_count", caption.Key);
        Assert.Equal(new object[] { 7 }, caption.Args);
    });

    [Fact]
    public Task RefusalsLeaveTheBalanceAndCalendarAlone() => RunAsync(async (tab, quests, settings) =>
    {
        settings.StreakFixCharges = 1;
        var button = tab.FindControl<Button>("BtnFixStreak")!;
        var status = tab.FindControl<IconLabel>("TxtFixStreakStatus")!;
        tab.RefreshQuestUI();
        Assert.False(button.IsEnabled);   // signed out: no cached balance spend
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("tooltip_streak_fixes_need_account"), ToolTip.GetTip(button));

        int calls = 0;
        CoreAccount.UseStreakFixProvider = _ => { calls++; return Task.FromResult((false, (string?)"nope", (int?)null)); };
        tab.ConfirmFix = (_, _) => Task.FromResult(true);
        var day = DateTime.Today.AddDays(-1);
        await tab.SpendStreakFixAsync(day);
        Assert.Equal(0, calls);
        Assert.Equal("label_oopsie_insurance_requires_a_cloud_account_ple", status.Key);

        settings.UnifiedId = "u1";
        tab.ConfirmFix = (_, _) => Task.FromResult(false);
        await tab.SpendStreakFixAsync(day);
        Assert.Equal(0, calls);   // declined at the confirmation

        tab.ConfirmFix = (_, _) => Task.FromResult(true);
        await tab.SpendStreakFixAsync(day);
        Assert.Equal(1, calls);
        Assert.Equal("nope", status.Text);   // the server's own error text
        Assert.Equal(1, settings.StreakFixCharges);
        Assert.Empty(quests.Progress.DailyQuestCompletionDates);
    });

    private static Task RunAsync(Func<QuestsTabView, QuestService, AppSettings, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var root = Directory.CreateTempSubdirectory("quest-fix-").FullName;
            var oldProvider = CoreSettings.ServiceProvider;
            var oldQuests = AvApp.Quests;
            var oldDefinitions = AvApp.QuestDefinitions;
            var oldSpend = CoreAccount.UseStreakFixProvider;
            AvApp.QuestDefinitions = null;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var quests = new QuestService(null, root);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            AvApp.Quests = quests;
            var tab = new QuestsTabView();
            var host = new Window { Content = tab, Width = 1200, Height = 900 };
            host.Show();
            host.UpdateLayout();   // the calendar paints only at a measured width
            Dispatcher.UIThread.RunJobs();
            try { await body(tab, quests, settings.Current); }
            finally
            {
                host.Close();
                quests.Dispose();
                AvApp.Quests = oldQuests;
                AvApp.QuestDefinitions = oldDefinitions;
                CoreAccount.UseStreakFixProvider = oldSpend;
                CoreSettings.ServiceProvider = oldProvider;
                Directory.Delete(root, true);
            }
        });
}
