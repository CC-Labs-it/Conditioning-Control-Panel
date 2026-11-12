using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
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
        public bool Throw; public int Writes; private int _volume;
        public int Volume { get => _volume; set { Writes++; if (Throw) throw new InvalidOperationException("volume failed"); _volume = value; } }
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
            s.IntensityRampEnabled = true; s.RampLinkFlashOpacity = true; s.RampLinkSpiralOpacity = true;
            s.FlashOpacity = 30; s.SpiralOpacity = 20; s.SchedulerMultiplier = 2;
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
            int writes = player.Writes; shell.StopEngineRamp();
            Assert.Equal(writes, player.Writes); // no ramp must not touch a playing bed
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
                var refreshes = SpiralOverlay.Refreshes;
                now = now.AddMinutes(5); shell.TickEngineRamp(); Assert.Equal(45, s.FlashOpacity);
                Assert.Equal(30, s.SpiralOpacity); Assert.True(SpiralOverlay.Refreshes > refreshes); // linked overlay repaints
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
                var tint = new TintOverlayWindow(); tint.Show(); // seed the headless tint registry
                ((System.Collections.IList)typeof(PinkFilterOverlay).GetField("Windows",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!).Add(tint);
                player.Throw = true; MainShellWindow.StopEngine(); // Core catches the simulated native volume failure
                Assert.False(tint.IsVisible);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
                player.Throw = false;
                shell.Close();
                Assert.False(shell.Ramp.IsActive); Assert.Equal(30, s.FlashOpacity);
                Assert.False(File.Exists(journal));
            }
            finally { player.Throw = false; MainShellWindow.StopEngine(); shell.Close(); PinkFilterOverlay.CloseAll(); File.Delete(file); }
        });
    }

    [Fact]
    public async Task RampRepaintsVisibleOverlaysOnlyOutsideSession()
    {
        var gif = Path.Combine(Path.GetTempPath(), $"ccp-ramp-{Guid.NewGuid()}.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(
            "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7"));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
                var s = service.Current;
                s.IntensityRampEnabled = true; s.RampDurationMinutes = 10; s.SchedulerMultiplier = 2;
                s.RampLinkPinkFilterOpacity = true; s.RampLinkSpiralOpacity = true;
                s.PinkFilterEnabled = true; s.PinkFilterOpacity = 20;
                s.SpiralEnabled = true; s.SpiralOpacity = 20; s.SpiralPath = gif;
                var shell = new MainShellWindow(); shell.Show();
                var runner = ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
                var provider = CoreSession.IsEngineRunningProvider;
                CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                PinkFilterOverlay.SkipPlatformChecksForTests = true; SpiralOverlay.SkipPlatformChecksForTests = true;
                var now = new DateTime(2026, 10, 19); shell.EngineNow = () => now;
                try
                {
                    while (SpiralOverlay.Decoding is { IsCompleted: false } pending) await pending;
                    shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                    while (SpiralOverlay.Decoding is { IsCompleted: false } pending) await pending;
                    Assert.NotEmpty(PinkFilterOverlay.Shown); Assert.NotEmpty(SpiralOverlay.Shown);
                    var pink = PinkFilterOverlay.Shown[0]; var spiral = SpiralOverlay.Shown[0];
                    byte Alpha() => ((SolidColorBrush)((Border)pink.Content!).Background!).Color.A;
                    Assert.True(pink.IsVisible); Assert.True(spiral.IsVisible);
                    Assert.Equal((byte)(0.2 * 255), Alpha());
                    Assert.Equal(SpiralFrames.Paint(0.2), spiral.Spiral.Opacity, 10);
                    now = now.AddMinutes(5); shell.TickEngineRamp();
                    Assert.Same(pink, PinkFilterOverlay.Shown[0]); Assert.Same(spiral, SpiralOverlay.Shown[0]);
                    Assert.Equal((byte)(0.3 * 255), Alpha());
                    Assert.Equal(SpiralFrames.Paint(0.3), spiral.Spiral.Opacity, 10);

                    shell.StartSession(new ConditioningControlPanel.Models.Session { Id = "gate", Name = "Gate", DurationMinutes = 10 });
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(runner.IsRunning);
                    var pinkBefore = Alpha(); var spiralBefore = spiral.Spiral.Opacity;
                    var pinkSetting = s.PinkFilterOpacity; var spiralSetting = s.SpiralOpacity;
                    var refreshes = SpiralOverlay.Refreshes;
                    now = now.AddMinutes(2); shell.TickEngineRamp();
                    Assert.Equal(pinkSetting, s.PinkFilterOpacity); Assert.Equal(spiralSetting, s.SpiralOpacity);
                    Assert.Equal(pinkBefore, Alpha()); Assert.Equal(spiralBefore, spiral.Spiral.Opacity);
                    Assert.Equal(refreshes, SpiralOverlay.Refreshes);
                }
                finally
                {
                    runner.Stop(completed: false); MainShellWindow.StopEngine(); shell.Close();
                    PinkFilterOverlay.CloseAll(); SpiralOverlay.CloseAll();
                    PinkFilterOverlay.SkipPlatformChecksForTests = false; SpiralOverlay.SkipPlatformChecksForTests = false;
                    CoreSession.IsEngineRunningProvider = provider;
                }
            });
        }
        finally { File.Delete(gif); }
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
                engine.Reset();
                var runner = ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
                shell.StartSession(new ConditioningControlPanel.Models.Session { Id = "stop", Name = "Stop", DurationMinutes = 10 });
                var stop = shell.Named<PresetsTabView>("PresetsTab")!.BtnStartSession;
                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                shell.OwnedWindows.OfType<MessageDialog>().Single().Close(false); Dispatcher.UIThread.RunJobs();
                Assert.True(runner.IsRunning); Assert.Null(engine.Progress.LastPanicPressTime);
                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                shell.OwnedWindows.OfType<MessageDialog>().Single().Close(true); Dispatcher.UIThread.RunJobs();
                Assert.False(runner.IsRunning); Assert.NotNull(engine.Progress.LastPanicPressTime);
                shell.StartEngineFromUser(); Dispatcher.UIThread.RunJobs();
                Assert.True(engine.Progress.IsUnlocked("relapse"));
            }
            finally { MainShellWindow.StopEngine(); shell.Close(); }
        });
    }
}
