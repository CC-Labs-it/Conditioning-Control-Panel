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
    private sealed class RampPlayer : ConditioningControlPanel.Avalonia.Platform.LayeredAudio.ILayerPlayer
    {
        public int Volume { get; set; }
        public void Dispose() { }
    }

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
            var notices = new System.Collections.Generic.List<(string, string)>();
            ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (title, body) =>
            {
                Assert.True(CoreEngine.IsRunning); // notice precedes the automatic stop
                notices.Add((title, body));
            };
            s.MasterVolume = 40; s.RampLinkMasterAudio = true; s.AudioLayersMasterVolume = 100;
            var file = Path.GetTempFileName();
            var player = new RampPlayer();
            using var layers = new ConditioningControlPanel.Avalonia.Platform.LayeredAudio(_ => player);
            ConditioningControlPanel.Avalonia.Platform.LayeredAudio.Instance = layers;
            s.AudioLayersEnabled = true;
            s.AudioLayers = new() { new() { Path = file, Volume = 100 } };
            layers.Start();
            shell.ShowTab("appsettings"); Dispatcher.UIThread.RunJobs();
            var audio = shell.AppSettingsPage!.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.AudioSettingsSection>("SectionAudio")!;
            void AssertAudio(int volume)
            {
                Assert.Equal(volume, audio.FindControl<Slider>("SliderMaster")!.Value);
                Assert.Equal($"{volume}%", audio.FindControl<TextBlock>("TxtMaster")!.Text);
                Assert.True(layers.IsPlaying);
                Assert.Equal((int)Math.Round(Math.Cbrt(volume / 100f) * 100), player.Volume);
            }
            var journal = Path.Combine(CorePaths.UserData, SessionSettingsJournal.RampFileName);
            try
            {
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                Assert.True(CoreEngine.IsRunning); Assert.True(shell.Ramp.IsActive);
                Assert.True(File.Exists(journal));
                now = now.AddMinutes(5); shell.TickEngineRamp(); Assert.Equal(45, s.FlashOpacity);
                AssertAudio(60); Assert.Empty(notices);
                MainShellWindow.StopEngine();
                Assert.False(shell.Ramp.IsActive); Assert.Equal(30, s.FlashOpacity);
                Assert.False(File.Exists(journal));
                AssertAudio(40); Assert.Empty(notices);
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                now = now.AddMinutes(10); shell.TickEngineRamp();
                Assert.False(CoreEngine.IsRunning); Assert.Equal(30, s.FlashOpacity);
                Assert.False(File.Exists(journal));
                AssertAudio(40);
                Assert.Equal((ConditioningControlPanel.Localization.Loc.Get("dialog_session_complete"),
                    ConditioningControlPanel.Localization.Loc.Get("ramp_complete_stopping")), Assert.Single(notices));
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                s.EndSessionOnRampComplete = false;
                now = now.AddMinutes(10); shell.TickEngineRamp();
                Assert.True(CoreEngine.IsRunning); Assert.Single(notices);
                shell.Close();
                Assert.False(shell.Ramp.IsActive); Assert.Equal(30, s.FlashOpacity);
                Assert.False(File.Exists(journal));
            }
            finally { MainShellWindow.StopEngine(); shell.Close(); File.Delete(file); }
        });
    }

    [Fact]
    public void AutomaticCompletionNotifiesBeforeStopping()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
            var shell = new MainShellWindow(); shell.Show(); CoreEngine.StoppedHook = shell.OnEngineStopped;
            var s = CoreSettings.Current; s.IntensityRampEnabled = true; s.RampDurationMinutes = 10;
            s.EndSessionOnRampComplete = true;
            var now = DateTime.Now; shell.EngineNow = () => now;
            var notices = new System.Collections.Generic.List<(string, string, bool)>();
            ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (t, b) => notices.Add((t, b, CoreEngine.IsRunning));
            try
            {
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                now = now.AddMinutes(10); shell.TickEngineRamp();
                Assert.Equal((ConditioningControlPanel.Localization.Loc.Get("dialog_session_complete"),
                    ConditioningControlPanel.Localization.Loc.Get("ramp_complete_stopping"), true), Assert.Single(notices));
                Assert.False(CoreEngine.IsRunning);
                shell.TickEngineRamp(); Assert.Single(notices);
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
            s.EndSessionOnRampComplete = true; s.RampDurationMinutes = 10;
            var now = DateTime.Now; shell.EngineNow = () => now;
            int notices = 0;
            ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => notices++;
            var session = new ConditioningControlPanel.Models.Session { Id = "ramp", Name = "Ramp", DurationMinutes = 10 };
            session.Settings.AudioWhispersEnabled = true; session.Settings.WhisperVolume = 67;
            try
            {
                shell.StartSession(session); Dispatcher.UIThread.RunJobs();
                Assert.True(runner.IsRunning); Assert.True(shell.Ramp.IsActive);
                Assert.Equal(23d, shell.Ramp.Bases["SubAudioVolume"]);
                Assert.Equal(67, s.SubAudioVolume);
                now = now.AddMinutes(11); shell.TickEngineRamp();
                Assert.True(CoreEngine.IsRunning); Assert.Equal(0, notices);
                runner.Stop(completed: false);
                Assert.Equal(0, notices);
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
