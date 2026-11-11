using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionPinkAiTests
{
    [Fact]
    public void Pink_on_starts_with_engine_off_saves_and_stop_clears_hold()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            s.CompanionPrompt.AllowAiToControlEffects = true;
            s.SpiralEnabled = false;
            s.PinkFilterEnabled = false;
            CoreAccount.HasLabAccessProvider = () => true;
            CoreSession.IsEngineRunningProvider = () => false;
            var host = new Window();
            CoreEngine.StoppedHook = () =>
            {
                global::ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(false);
                PinkFilterOverlay.Refresh(host);
            };
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            CompanionEffects.StartEffect = action => action();
            host.Show();
            try
            {
                Assert.True(CompanionEffects.ApplyPink(host, true, 25));
                Assert.True(PinkFilterOverlay.IsShowing);
                Assert.True(PinkFilterOverlay.AiRunning);
                Assert.True(SpiralOverlay.AiRunning);
                Assert.Equal((true, 25), (s.PinkFilterEnabled, s.PinkFilterOpacity));
                Assert.True(service.HasPendingSave);
                MainShellWindow.StopEngine();
                Assert.False(PinkFilterOverlay.AiRunning || PinkFilterOverlay.IsShowing);
            }
            finally
            {
                service.SealForReset();
                PinkFilterOverlay.StopForAi();
                PinkFilterOverlay.CloseAll();
                SpiralOverlay.CloseAll();
                PinkFilterOverlay.SkipPlatformChecksForTests = false;
                host.Close();
            }
        });
    }

    [Fact]
    public void Pink_on_without_click_through_restores_settings()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            s.CompanionPrompt.AllowAiToControlEffects = true;
            (s.PinkFilterEnabled, s.PinkFilterOpacity) = (false, 10);
            CoreAccount.HasLabAccessProvider = () => true;
            CoreSession.IsEngineRunningProvider = () => false;
            CompanionEffects.StartEffect = action => action();
            var host = new Window();
            host.Show();
            try
            {
                Assert.False(CompanionEffects.ApplyPink(host, true, 25));
                Assert.Equal((false, 10), (s.PinkFilterEnabled, s.PinkFilterOpacity));
                Assert.False(PinkFilterOverlay.IsShowing || PinkFilterOverlay.AiRunning || service.HasPendingSave);
            }
            finally
            {
                service.SealForReset();
                PinkFilterOverlay.StopForAi();
                PinkFilterOverlay.CloseAll();
                host.Close();
            }
        });
    }
}
