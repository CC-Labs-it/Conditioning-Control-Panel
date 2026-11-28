using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class LoginStreakBonusPopupTests
{
    [Fact]
    public void PaidBonusShowsOnceAndPanicClosesOrCancelsIt() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-streak-popup-").FullName;
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        settings.Current.OfflineMode = true;
        settings.Current.OfflineUsername = "test";
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            engine.Progress.ConsecutiveDays = 5;
            engine.Progress.PendingStreakBonus = true;
            LoginStreakHead.QuestXpReady(engine);
            LoginStreakHead.QuestXpReady(engine);
            Dispatcher.UIThread.RunJobs();
            var field = typeof(AvApp).GetField("_perfectWeekPopups", BindingFlags.Static | BindingFlags.NonPublic)!;
            var popups = (IList)field.GetValue(null)!;
            var popup = Assert.IsType<AchievementPopup>(Assert.Single(popups));
            Assert.True(popup.IsVisible);
            Assert.Equal(Loc.GetF("skill_streak_bonus_name", 5), popup.FindControl<TextBlock>("TxtName")!.Text);
            Assert.Equal(Loc.GetF("skill_streak_bonus_flavor", 100, 5), popup.FindControl<TextBlock>("TxtFlavor")!.Text);
            Assert.Equal(Loc.Get("skill_daily_streak_bonus_header"), popup.FindControl<TextBlock>("TxtHeaderText")!.Text);
            Assert.NotNull(popup.FindControl<Image>("AchievementImage")!.Source);
            var gift = ConditioningControlPanel.Avalonia.Controls.IconMap.ForSkill("milestone_rewards")!.Value;
            Assert.Equal(gift.Kind, popup.FindControl<ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtHeaderIcon")!.Kind);
            var panic = Assert.Single(PanicSurfaces.All, s => s.Id == "perfect-week-bonus");
            panic.Stop(null);
            Assert.False(popup.IsVisible);
            engine.Progress.PendingStreakBonus = true;
            LoginStreakHead.QuestXpReady(engine);
            panic.Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(popups);
        }
        finally
        {
            AvApp.StopPerfectWeekBonus();
            LoginStreakHead.Stop();
            CoreSettings.ServiceProvider = oldSettings;
            Directory.Delete(dir, true);
        }
    });
}
