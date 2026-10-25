using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class UiUpdatesTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                        .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
                { UseHeadlessDrawing = false }).SetupWithoutStarting();
        global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public void StatRefreshRepairsMicPillAndRebuildsXpBonusChips() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var s = CoreSettings.Current;
        var skills = s.UnlockedSkills.ToList();
        var (consent, wake) = (s.MicConsentGiven, s.SpeechWakeWordEnabled);
        var shell = new MainShellWindow();
        try
        {
            s.UnlockedSkills.Clear();
            s.UnlockedSkills.Add("sparkle_boost_1");
            s.MicConsentGiven = s.SpeechWakeWordEnabled = true;
            shell.Named<Border>("MicActivePill")!.IsVisible = false;
            shell.UpdateStatPills();
            Assert.True(shell.Named<Border>("MicActivePill")!.IsVisible);
            var chip = Assert.Single(shell.Named<StackPanel>("XPBarBonusList")!.Children.OfType<Border>());
            Assert.Equal("+10% Sparkle Boost", Assert.IsType<TextBlock>(chip.Child).Text);
            Assert.Equal(new Thickness(6, 3, 6, 3), chip.Padding);
            Assert.Equal("Skill tree bonus: +10% XP from Sparkle Boost", ToolTip.GetTip(chip));
            s.MicConsentGiven = false;
            s.UnlockedSkills.Clear();
            shell.UpdateStatPills();
            Assert.False(shell.Named<Border>("MicActivePill")!.IsVisible);
            Assert.Empty(shell.Named<StackPanel>("XPBarBonusList")!.Children);
        }
        finally
        {
            shell.Close();
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            (s.MicConsentGiven, s.SpeechWakeWordEnabled) = (consent, wake);
            s.UnlockedSkills.Clear(); s.UnlockedSkills.AddRange(skills);
        }
    });

    [Fact]
    public void ModChangeHidesJournalAndReturnsAnOpenJournalToDaily() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var old = CoreMods.ActiveModIdProvider;
        string id = BuiltInMods.CCPDefaultId;
        CoreMods.ActiveModIdProvider = () => id;
        var view = new QuestsTabView();
        var host = new Window { Content = view };
        try
        {
            host.Show();
            var button = view.FindControl<Button>("BtnQuestSubRoadmap")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(view.FindControl<Control>("RoadmapPanel")!.IsVisible);
            id = "community-mod";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = id }, null, false));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsVisible);
            Assert.False(view.FindControl<Control>("RoadmapPanel")!.IsVisible);
            Assert.True(view.FindControl<Control>("DailyWeeklyPanel")!.IsVisible);
            id = BuiltInMods.BambiSleepId;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = id }, null, false));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsVisible);
        }
        finally { host.Close(); CoreMods.ActiveModIdProvider = old; }
    });
}
