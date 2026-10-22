using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Quests tab's last WPF pieces (MainWindow.QuestsTab.cs): "Fix day" (:737-960: the button shows for
/// everyone and is armed only signed in, with a charge and a missed day; a press enters fix mode with rings on the
/// missed days; a ring asks, spends on the server and only then stamps the day and takes the charge; a balance move
/// repaints), the server season title (:205) and the quest art on the cards (:177, :296).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class QuestsStreakFixTests
{
    [Fact]
    public Task FixDayArmsEntersFixModeAndSpendsOnlyOnAServerYes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-quests-fix-").FullName;
        var oldQuests = AvApp.Quests;
        var oldDefs = AvApp.QuestDefinitions;
        var oldProvider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var service = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var s = ConditioningControlPanel.CoreSettings.Current;
        Window? host = null;
        QuestService? quests = null;
        try
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            LocalizationManager.Instance.SetLanguage("en");

            // A fixed mid-month "today", so the run does not depend on the calendar (the 1st has no missed day).
            var today = new DateTime(2026, 3, 15);
            s.UnifiedId = null;
            s.StreakFixCharges = 2;
            s.StreakShieldUsedDates?.Clear();
            quests = new QuestService(null, dir);
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            quests.Progress.DailyQuestCompletionDates.Clear();
            AvApp.Quests = quests;
            var defs = new QuestDefinitionService(Path.Combine(dir, "defs"));
            AvApp.QuestDefinitions = defs;

            var tab = new QuestsTabView { Today = () => new DateTime(2026, 3, 1) };
            host = new Window { Width = 1200, Height = 900, Content = tab };
            host.Show();
            Dispatcher.UIThread.RunJobs();

            // The season title is the definitions' (server title, else the local month name), not the placeholder.
            Assert.Equal(defs.SeasonTitle, tab.FindControl<TextBlock>("TxtSeasonTitle")!.Text);
            // The cards and the weekly wear their quest art.
            Assert.IsType<Bitmap>(tab.FindControl<DailyQuestCard>("DailyCard0")!.FindControl<Image>("Art")!.Source);
            Assert.IsType<Bitmap>(tab.FindControl<Image>("ImgWeeklyQuest")!.Source);

            var button = tab.FindControl<Button>("BtnFixStreak")!;
            var caption = tab.FindControl<IconLabel>("FixStreakLabel")!;
            var status = tab.FindControl<IconLabel>("TxtFixStreakStatus")!;

            // Shown for everyone; signed out it is greyed with the reason.
            Assert.True(button.IsVisible);
            Assert.False(button.IsEnabled);
            Assert.Equal(Loc.GetF("btn_fix_day_with_count", 2), caption.Source);
            Assert.Equal(Loc.Get("tooltip_streak_fixes_need_account"), ToolTip.GetTip(button));

            s.UnifiedId = "u_k2";
            tab.RefreshQuestUI();
            // The first of the month has no missed day to fix.
            Assert.False(button.IsEnabled);
            Assert.Equal(Loc.Get("tooltip_no_missed_days_your_streak_is_perfect"), ToolTip.GetTip(button));

            tab.Today = () => today;
            tab.RefreshQuestUI();
            Assert.True(button.IsEnabled);
            Assert.Equal(Loc.GetF("tooltip_use_streak_fix", 2), ToolTip.GetTip(button));

            // A press enters fix mode: every missed day wears a ring, the button is the way out.
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsStreakFixMode);
            Assert.Equal(today.Day - 1, tab.StreakFixRings.Count);
            Assert.Equal(Loc.Get("btn_cancel_2"), caption.Source);
            Assert.Equal(Loc.Get("label_click_a_missed_day_to_fix_it_free"), status.Source);
            Assert.True(status.IsVisible);
            // The rings pulse 1.0 -> 0.3 over 600 ms (clamped), on the tab's ambient beat.
            tab.StepFixPulse(0.6);
            Assert.Equal(0.3, tab.StreakFixRings[0].Opacity, 3);
            tab.StepFixPulse(1.2);
            Assert.Equal(1.0, tab.StreakFixRings[0].Opacity, 3);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.IsStreakFixMode);
            Assert.Empty(tab.StreakFixRings);
            Assert.False(status.IsVisible);

            // "No" at the question spends nothing.
            var spent = new List<DateTime>();
            bool answer = false;
            (bool, string?, int?) reply = (false, "Server says no", null);
            tab.SpendStreakFix = day => { spent.Add(day); return Task.FromResult(reply); };
            var missed = today.AddDays(-1);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            // The user's way in: a left press on the ring over the missed day (its PointerPressed).
            var asked = new List<string>();
            tab.AskStreakFix = (_, _, message) => { asked.Add(message); return Task.FromResult(answer); };
            var ring = Assert.Single(tab.StreakFixRings, r => (DateTime)r.Tag! == missed);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            host.MouseDown(ring.TranslatePoint(new Point(ring.Bounds.Width / 2, ring.Bounds.Height / 2), host)!.Value, MouseButton.Left);
            host.MouseUp(ring.TranslatePoint(new Point(ring.Bounds.Width / 2, ring.Bounds.Height / 2), host)!.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { Loc.GetF("label_fix_day_confirm_body", missed.ToString("MMMM d"), 2) }, asked);
            Assert.Empty(spent);
            Assert.Equal(2, s.StreakFixCharges);

            // A server refusal: the reason shown, the charge and the day untouched.
            answer = true;
            await tab.FixDayAsync(missed);
            Assert.Single(spent);
            Assert.Equal(2, s.StreakFixCharges);
            Assert.DoesNotContain(quests.Progress.DailyQuestCompletionDates, d => d.Date == missed);
            Assert.Contains("Server says no", status.Source);
            Assert.True(tab.IsStreakFixMode);

            // A server yes: its balance, the day stamped once, fix mode over, the stats tile repainted.
            reply = (true, null, 1);
            await tab.FixDayAsync(missed);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, s.StreakFixCharges);
            Assert.Single(quests.Progress.DailyQuestCompletionDates, d => d.Date == missed);
            Assert.False(tab.IsStreakFixMode);
            Assert.Equal(Loc.GetF("label_streak_fixed_success", missed.ToString("MMMM d")), status.Source);
            Assert.Equal("1", tab.FindControl<TextBlock>("TxtStreakFixCharges")!.Text);
            Assert.Equal(Loc.GetF("btn_fix_day_with_count", 1), caption.Source);

            // A balance move from elsewhere (sync adoption) repaints the parked tab.
            s.StreakFixCharges = 5;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("5", tab.FindControl<TextBlock>("TxtStreakFixCharges")!.Text);
            Assert.Equal(Loc.GetF("btn_fix_day_with_count", 5), caption.Source);
        }
        finally
        {
            try { host?.Close(); } catch { }
            quests?.Dispose();
            service.SaveImmediate();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldProvider;
            AvApp.Quests = oldQuests;
            AvApp.QuestDefinitions = oldDefs;
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    });
}
