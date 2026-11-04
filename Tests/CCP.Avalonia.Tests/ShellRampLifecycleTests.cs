using System;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

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
}
