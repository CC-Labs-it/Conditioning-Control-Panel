using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellLevelCallerTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void PlayerLevelAward_reaches_the_notice_and_sound_caller() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var oldSink = OsNotifications.Sink;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        settings.Current.OfflineUsername = "tester";
        settings.Current.PlayerLevel = 1;
        settings.Current.PlayerXP = 0;
        var seen = new System.Collections.Generic.List<string>();
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { seen.Add("sound"); return () => { }; };
        OsNotifications.Sink = (title, _) => seen.Add(title == Loc.Get("toast_level_up_title") ? "player-notice" : "other-notice");
        CoreSettings.Current.ActiveCompanionId = (int)ConditioningControlPanel.Models.CompanionId.CultBunny;
        CoreSettings.Current.CompanionPerk = ConditioningControlPanel.Models.CompanionBonusType.PinkFilterBonus;
        CoreSettings.Current.PinkFilterEnabled = false;
        ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
        var shell = new MainShellWindow();
        try
        {
            ProgressionBank.Add(XpCurve.GetXPForLevel(1, XpCurve.EpochOf(settings.Current)), "ShellNotificationTest");
            Dispatcher.UIThread.RunJobs();
            Assert.True(settings.Current.PlayerLevel > 1);
            int notice = seen.IndexOf("player-notice");
            Assert.True(notice >= 0);
            Assert.Equal("sound", seen[notice + 1]);
            var progress = CoreSettings.Current.ActiveCompanionProgress;
            progress.Level = 9;
            progress.CurrentXP = 0;
            seen.Clear();
            ConditioningControlPanel.Services.Companion.CompanionCore.AddCompanionXP(progress.XPForNextLevel, "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "other-notice", "sound" }, seen);
            seen.Clear();
            progress.Level = 19;
            progress.CurrentXP = 0;
            ConditioningControlPanel.Services.Companion.CompanionCore.AddCompanionXP(progress.XPForNextLevel, "test");
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(seen); // a queued event cannot restart audio after panic
            // WPF MainWindow.Companion.cs:48-77: a fresh milestone still notifies during quiet.
            progress.Level = 29;
            progress.CurrentXP = 0;
            ConditioningControlPanel.Services.Companion.CompanionCore.AddCompanionXP(progress.XPForNextLevel, "test");
            Dispatcher.UIThread.RunJobs();
            Assert.True(ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced());
            Assert.Equal(new[] { "other-notice", "sound" }, seen);
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            int stops = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => stops++;
            MainShellWindow.PlayLevelUpSound();
            // Queue a real level event before close, then drain it after close.
            progress.Level = 19;
            progress.CurrentXP = 0;
            ConditioningControlPanel.Services.Companion.CompanionCore.AddCompanionXP(progress.XPForNextLevel, "test");
            shell.Close();
            Assert.Equal(1, stops);
            seen.Clear();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(seen);
            progress.Level = 29;
            progress.CurrentXP = 0;
            ConditioningControlPanel.Services.Companion.CompanionCore.AddCompanionXP(progress.XPForNextLevel, "test");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(seen);
        }
        finally
        {
            shell.Close();
            MainShellWindow.StopLevelUpSound();
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = oldSettings;
            CoreAudio.PlayStoppableProvider = oldAudio;
            OsNotifications.Sink = oldSink;
            CoreSettings.SaveImmediate();
        }
    });

}
