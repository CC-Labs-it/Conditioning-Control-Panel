using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EngineRampLifecycleTests
{
    [Fact]
    public void RealStartStopRestoresAndJournalsRamp()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            s.IntensityRampEnabled = true; s.RampLinkFlashOpacity = true;
            s.FlashOpacity = 30; s.SchedulerMultiplier = 2;
            s.RampDurationMinutes = 10; s.EndSessionOnRampComplete = true;
            var shell = new MainShellWindow(); shell.Show();
            var now = new DateTime(2026, 10, 19, 17, 0, 0);
            shell.EngineNow = () => now;
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            var journal = Path.Combine(CorePaths.UserData, SessionSettingsJournal.RampFileName);
            try
            {
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                Assert.True(CoreEngine.IsRunning); Assert.True(shell.Ramp.IsActive);
                Assert.True(File.Exists(journal));
                now = now.AddMinutes(5); shell.TickEngineRamp(); Assert.Equal(45, s.FlashOpacity);
                MainShellWindow.StopEngine();
                Assert.False(shell.Ramp.IsActive); Assert.Equal(30, s.FlashOpacity);
                Assert.False(File.Exists(journal));
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                now = now.AddMinutes(10); shell.TickEngineRamp();
                Assert.False(CoreEngine.IsRunning); Assert.Equal(30, s.FlashOpacity);
                Assert.False(File.Exists(journal));
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                now = now.AddMinutes(5); shell.TickEngineRamp();
                shell.Close();
                Assert.False(shell.Ramp.IsActive); Assert.Equal(30, s.FlashOpacity);
                Assert.False(File.Exists(journal));
            }
            finally { MainShellWindow.StopEngine(); shell.Close(); }
        });
    }

    [Fact]
    public void PresetStopRestoresUserValuesRatherThanPresetRampBases()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var shell = new MainShellWindow(); shell.Show();
            var runner = ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            var s = CoreSettings.Current;
            s.IntensityRampEnabled = true; s.SubAudioVolume = 23;
            var session = new ConditioningControlPanel.Models.Session { Id = "ramp", Name = "Ramp", DurationMinutes = 10 };
            session.Settings.AudioWhispersEnabled = true; session.Settings.WhisperVolume = 67;
            try
            {
                shell.StartSession(session); Dispatcher.UIThread.RunJobs();
                Assert.True(runner.IsRunning); Assert.True(shell.Ramp.IsActive);
                Assert.Equal(23d, shell.Ramp.Bases["SubAudioVolume"]);
                Assert.Equal(67, s.SubAudioVolume);
                runner.Stop(completed: false);
                Assert.Equal(23, s.SubAudioVolume);
                Assert.False(shell.Ramp.IsActive);
                Assert.False(File.Exists(Path.Combine(CorePaths.UserData, SessionSettingsJournal.RampFileName)));
            }
            finally { runner.Stop(completed: false); MainShellWindow.StopEngine(); shell.Close(); }
        });
    }

    [Fact]
    public void ManualStopAndPanicStampRelapseButAutomaticStartDoesNotUnlock()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var field = typeof(ConditioningControlPanel.Avalonia.App).GetField("<Achievements>k__BackingField",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(CorePaths.UserData, "relapse-test.json")));
            field.SetValue(null, engine);
            var shell = new MainShellWindow(); shell.Show();
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            try
            {
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                shell.Named<Button>("BtnStart")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(engine.Progress.LastPanicPressTime);
                shell.StartEngine(); Dispatcher.UIThread.RunJobs();
                Assert.False(engine.Progress.IsUnlocked("relapse"));
                engine.Progress.LastPanicPressTime = null;
                shell.HandlePanicKeyPress(DateTime.Now);
                Assert.False(CoreEngine.IsRunning);
                Assert.NotNull(engine.Progress.LastPanicPressTime);
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                Assert.True(engine.Progress.IsUnlocked("relapse"));
            }
            finally { MainShellWindow.StopEngine(); shell.Close(); }
        });
    }
}
