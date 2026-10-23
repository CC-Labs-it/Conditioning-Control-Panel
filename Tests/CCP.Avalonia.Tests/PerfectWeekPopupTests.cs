using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class PerfectWeekPopupTests
{
    [Fact]
    public void SeedGrantsOnce_ShowsWpfCopy_AndPanicClosesOrCancelsPresentation() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGrant = CoreProgression.AddXPProvider;
        var oldBonus = CoreQuests.CheckPerfectWeekBonusProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.UnlockedSkills.Add("perfect_bimbo_week");
        s.DailyQuestStreak = 7;
        s.PlayerLevel = 10;
        s.LastPerfectWeekStreakAwarded = 0;
        int paid = 0;
        CoreProgression.AddXPProvider = (amount, source) => { Assert.Equal("Other", source); paid += (int)amount; };
        try
        {
            AvApp.SeedPerfectWeekBonus();
            var reward = CoreQuests.CheckPerfectWeekBonusProvider!;
            Assert.Equal(3600, reward());
            Assert.Equal(0, reward());
            Assert.Equal(3600, paid);
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.IsType<AchievementPopup>(AvApp.PerfectWeekPopup);
            Assert.True(popup.IsVisible);
            Assert.Equal(Loc.Get("skill_perfect_week_name_popup"), popup.FindControl<TextBlock>("TxtName")!.Text);
            Assert.Equal(Loc.GetF("skill_perfect_week_flavor_popup", 7, 3600), popup.FindControl<TextBlock>("TxtFlavor")!.Text);
            var panic = Assert.Single(PanicSurfaces.All, surface => surface.Id == "perfect-week");
            panic.Stop(null);
            Assert.False(popup.IsVisible);
            Assert.Null(AvApp.PerfectWeekPopup);
            s.DailyQuestStreak = 14;
            Assert.Equal(7200, reward());
            panic.Stop(null); // panic before the dispatcher presents it
            Dispatcher.UIThread.RunJobs();
            Assert.Null(AvApp.PerfectWeekPopup);
            Assert.Equal(10800, paid); // panic never takes the earned reward away
        }
        finally
        {
            AvApp.ClosePerfectWeekPopup();
            CoreSettings.ServiceProvider = oldSettings;
            CoreProgression.AddXPProvider = oldGrant;
            CoreQuests.CheckPerfectWeekBonusProvider = oldBonus;
        }
    });
}
