using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class ShellRampLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedValuesRestoreOnStopAndPanic(bool panic)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            (s.FlashOpacity, s.SpiralOpacity, s.PinkFilterOpacity, s.MasterVolume, s.SubAudioVolume, s.BrainDrainBlurStrength) = (20, 30, 20, 25, 35, 40);
            (s.RampLinkFlashOpacity, s.RampLinkSpiralOpacity, s.RampLinkPinkFilterOpacity, s.RampLinkMasterAudio, s.RampLinkSubliminalAudio, s.RampLinkBrainDrain) = (true, true, true, true, true, true);
            s.SchedulerMultiplier = 2;
            var shell = new MainShellWindow();
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            try
            {
                shell.StartRampTimer();
                shell.RampTick(1, false);
                Assert.Equal((40, 60, 40, 50, 70, 80), (s.FlashOpacity, s.SpiralOpacity, s.PinkFilterOpacity, s.MasterVolume, s.SubAudioVolume, s.BrainDrainBlurStrength));
                if (panic) PanicSurfaces.StopAll("test", shell); else MainShellWindow.StopEngine();
                Assert.Equal((20, 30, 20, 25, 35, 40), (s.FlashOpacity, s.SpiralOpacity, s.PinkFilterOpacity, s.MasterVolume, s.SubAudioVolume, s.BrainDrainBlurStrength));
            }
            finally { shell.StopRampTimer(); CoreEngine.StoppedHook = null; shell.Close(); }
        });
    }

    [Fact]
    public void CompletionAnnouncesBeforeStopAndPresetSessionGuardsVisualsAndStop()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            s.FlashOpacity = 20; s.MasterVolume = 25;
            s.RampLinkFlashOpacity = true; s.RampLinkMasterAudio = true;
            s.SchedulerMultiplier = 2; s.EndSessionOnRampComplete = true;
            var shell = new MainShellWindow();
            string order = "";
            OsNotifications.Sink = (_, _) => order += "notice ";
            CoreEngine.StoppedHook = () => { order += "stop"; shell.OnEngineStopped(); };
            try
            {
                shell.StartRampTimer();
                shell.RampTick(1, true);
                Assert.Equal(20, s.FlashOpacity);
                Assert.Equal(50, s.MasterVolume);
                Assert.Equal("", order);
                shell.RampTick(1, false);
                Assert.Equal("notice stop", order);
                Assert.Equal(20, s.FlashOpacity);
                Assert.Equal(25, s.MasterVolume);
            }
            finally { shell.StopRampTimer(); CoreEngine.StoppedHook = null; OsNotifications.Sink = null; shell.Close(); }
        });
    }
    [Fact]
    public void CompletionNoticeIsRequestedBeforeTheEngineStops()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.EndSessionOnRampComplete = true;
            var shell = new MainShellWindow();
            string order = "", title = "", body = "";
            OsNotifications.Sink = (t, b) => { order += "notice "; title = t; body = b; };
            CoreEngine.StoppedHook = () => { order += "stop"; shell.OnEngineStopped(); };
            try
            {
                shell.StartRampTimer();
                shell.RampTick(1, false);
                Assert.Equal("notice stop", order);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("dialog_session_complete"), title);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("ramp_completion_stopping"), body);
            }
            finally { shell.StopRampTimer(); CoreEngine.StoppedHook = null; OsNotifications.Sink = null; shell.Close(); }
        });
    }

    private sealed class FakeLayer : LayeredAudio.ILayerPlayer
    {
        public int Volume { get; set; }
        public void Dispose() { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresetStartKeepsTheOriginalRampAndItsAudio(bool alreadyRunning)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            s.MasterVolume = 25; s.FlashOpacity = 20;
            s.IntensityRampEnabled = true; s.RampLinkMasterAudio = true; s.RampLinkFlashOpacity = true;
            s.SchedulerMultiplier = 2; s.EndSessionOnRampComplete = true;
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            try
            {
                if (alreadyRunning)
                {
                    shell.StartEngine();
                    shell.RampTick(0.5, false);
                    Assert.Equal(37, s.MasterVolume);
                }
                shell.StartSession(new Session { Id = "ramp-test", Name = "Ramp test", DurationMinutes = 20 });
                Assert.True(runner.IsRunning);
                int flash = s.FlashOpacity;
                shell.RampTick(1, runner.IsRunning);
                Assert.True(runner.IsRunning && CoreEngine.IsRunning);
                Assert.Equal(flash, s.FlashOpacity);
                Assert.Equal(50, s.MasterVolume); // still based on the pre-session 25
                runner.Stop();
                Assert.Equal(25, s.MasterVolume);
            }
            finally
            {
                runner.Stop(); shell.StopRampTimer(); CoreEngine.StoppedHook = null;
                AvApp.Sessions = null; CoreEngine.Stop(); shell.Close();
            }
        });
    }

    [Fact]
    public void MasterRampRefreshesPlayingLayerAndVideoOnTickAndRestore()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            s.MasterVolume = 25; s.VideoVolume = 80; s.AudioLayersMasterVolume = 100;
            s.RampLinkMasterAudio = true; s.SchedulerMultiplier = 2;
            var shell = new MainShellWindow();
            var file = Path.GetTempFileName();
            s.AudioLayers = new List<AudioLayerTrack> { new() { Path = file, Volume = 100 } };
            var player = new FakeLayer();
            using var layers = new LayeredAudio(_ => player);
            LayeredAudio.Instance = layers;
            layers.Start(ignoreMasterToggle: true);
            var video = MandatoryVideoOverlay.Instance;
            int output = -1;
            video.VolumeSink = value => output = value;
            try
            {
                shell.StartRampTimer();
                shell.RampTick(1, false);
                var tickLayer = player.Volume;
                var tickVideo = output;
                shell.StopRampTimer();
                Assert.Equal(((int)Math.Round(Math.Cbrt(0.5) * 100), MandatoryVideoScheduler.EffectiveVolume(50, 80),
                    (int)Math.Round(Math.Cbrt(0.25) * 100), MandatoryVideoScheduler.EffectiveVolume(25, 80)),
                    (tickLayer, tickVideo, player.Volume, output));
            }
            finally
            {
                shell.StopRampTimer(); video.VolumeSink = null; LayeredAudio.Instance = null;
                shell.Close(); File.Delete(file);
            }
        });
    }

}
