using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
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
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var previous = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider,
                CoreSession.IsEngineRunningProvider, CoreEngine.StoppedHook, CompanionEffects.StartEffect,
                PinkFilterOverlay.SkipPlatformChecksForTests);
            var service = new SettingsService();
            var s = service.Current;
            var host = new Window();
            try
            {
                CoreSettings.ServiceProvider = () => service;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                s.SpiralEnabled = false;
                s.PinkFilterEnabled = false;
                CoreAccount.HasLabAccessProvider = () => true;
                CoreSession.IsEngineRunningProvider = () => false;
                CoreEngine.StoppedHook = () =>
                {
                    global::ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(false);
                    PinkFilterOverlay.Refresh(host);
                };
                PinkFilterOverlay.SkipPlatformChecksForTests = true;
                CompanionEffects.StartEffect = action => action();
                host.Show();
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
                PinkFilterOverlay.SkipPlatformChecksForTests = previous.SkipPlatformChecksForTests;
                host.Close();
                (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider,
                    CoreEngine.StoppedHook, CompanionEffects.StartEffect) =
                    (previous.ServiceProvider, previous.HasLabAccessProvider, previous.IsEngineRunningProvider,
                     previous.StoppedHook, previous.StartEffect);
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
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var previous = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider,
                CoreSession.IsEngineRunningProvider, CoreEngine.StoppedHook, CompanionEffects.StartEffect,
                PinkFilterOverlay.SkipPlatformChecksForTests);
            var service = new SettingsService();
            var s = service.Current;
            var host = new Window();
            try
            {
                CoreSettings.ServiceProvider = () => service;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                (s.PinkFilterEnabled, s.PinkFilterOpacity) = (false, 10);
                CoreAccount.HasLabAccessProvider = () => true;
                CoreSession.IsEngineRunningProvider = () => false;
                CompanionEffects.StartEffect = action => action();
                host.Show();
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
                PinkFilterOverlay.SkipPlatformChecksForTests = previous.SkipPlatformChecksForTests;
                (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider,
                    CoreEngine.StoppedHook, CompanionEffects.StartEffect) =
                    (previous.ServiceProvider, previous.HasLabAccessProvider, previous.IsEngineRunningProvider,
                     previous.StoppedHook, previous.StartEffect);
            }
        });
    }

    [Fact]
    public void Pink_tests_restore_their_global_delegates()
    {
        var previous = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider,
            CoreSession.IsEngineRunningProvider, CoreEngine.StoppedHook, CompanionEffects.StartEffect);
        Func<SettingsService?> settings = () => null;
        Func<bool> lab = () => false;
        Func<bool> engine = () => true;
        Action stopped = () => { };
        Action<Action> start = action => action();
        try
        {
            (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider,
                CoreEngine.StoppedHook, CompanionEffects.StartEffect) = (settings, lab, engine, stopped, start);
            Pink_on_starts_with_engine_off_saves_and_stop_clears_hold();
            Check();
            Pink_on_without_click_through_restores_settings();
            Check();
        }
        finally
        {
            (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider,
                CoreEngine.StoppedHook, CompanionEffects.StartEffect) = previous;
        }

        void Check()
        {
            Assert.Same(settings, CoreSettings.ServiceProvider);
            Assert.Same(lab, CoreAccount.HasLabAccessProvider);
            Assert.Same(engine, CoreSession.IsEngineRunningProvider);
            Assert.Same(stopped, CoreEngine.StoppedHook);
            Assert.Same(start, CompanionEffects.StartEffect);
        }
    }

    [Theory]
    [InlineData(false)] // stop
    [InlineData(true)]  // pause invalidates the same generation after confirmation
    public void Held_pink_start_is_discarded_when_overlay_generation_changes(bool pause)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var previous = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider,
                CoreSession.IsEngineRunningProvider, CoreEngine.StoppedHook, CompanionEffects.StartEffect,
                PinkFilterOverlay.SkipPlatformChecksForTests);
            var service = new SettingsService();
            var host = new Window();
            try
            {
                CoreSettings.ServiceProvider = () => service;
                CoreAccount.HasLabAccessProvider = () => true;
                CoreSession.IsEngineRunningProvider = () => false;
                CoreEngine.StoppedHook = null;
                service.Current.CompanionPrompt.AllowAiToControlEffects = true;
                (service.Current.PinkFilterEnabled, service.Current.PinkFilterOpacity) = (false, 10);
                PinkFilterOverlay.SkipPlatformChecksForTests = true;
                Action? held = null;
                CompanionEffects.StartEffect = action => held = action;
                host.Show();
                Assert.True(CompanionEffects.ApplyPink(host, true, 25));
                Assert.NotNull(held);
                if (pause) MainShellWindow.InvalidateOverlayStart();
                else MainShellWindow.StopEngine();
                held!();
                Assert.Equal((false, 10), (service.Current.PinkFilterEnabled, service.Current.PinkFilterOpacity));
                Assert.False(PinkFilterOverlay.IsShowing || PinkFilterOverlay.AiRunning || service.HasPendingSave);
            }
            finally
            {
                service.SealForReset();
                PinkFilterOverlay.StopForAi();
                PinkFilterOverlay.CloseAll();
                SpiralOverlay.CloseAll();
                host.Close();
                PinkFilterOverlay.SkipPlatformChecksForTests = previous.SkipPlatformChecksForTests;
                (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider, CoreSession.IsEngineRunningProvider,
                    CoreEngine.StoppedHook, CompanionEffects.StartEffect) =
                    (previous.ServiceProvider, previous.HasLabAccessProvider, previous.IsEngineRunningProvider,
                     previous.StoppedHook, previous.StartEffect);
            }
        });
    }
}
