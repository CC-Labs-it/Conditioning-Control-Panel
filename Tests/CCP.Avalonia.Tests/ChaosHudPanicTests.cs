using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHudPanicTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void PauseExpandsInputRegion() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig()));
        PixelRect region = default;
        hud.InputRegionOverride = rects => region = Assert.Single(rects);
        try
        {
            hud.Show(); hud.UpdateLayout();
            Assert.Equal(116, region.Width);
            hud.SetPausedUi(true);
            Assert.Equal((int)hud.Bounds.Width, region.Width);
        }
        finally { hud.Close(); }
    });

    [Fact]
    public void OpenRegistersPassive() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig()));
        bool passive = false;
        hud.SetPassive = w => passive = ReferenceEquals(w, hud);
        try { hud.Show(); Assert.True(passive); }
        finally { hud.Close(); }
    });

    [Fact]
    public void LadderPanicHoldsThenSurfacesAndStopPassClosesHeldRun() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var (enabled, key, overrides) = (settings.PanicKeyEnabled, settings.PanicKey, settings.PanicOverridesAll);
        var shell = new MainShellWindow();
        try
        {
            settings.PanicKeyEnabled = true; settings.PanicKey = "F8"; settings.PanicOverridesAll = false;
            shell.Show();
            void Start()
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
            }
            Start();
            shell.HandlePanicKeyPress(DateTime.UtcNow);
            Assert.True(ChaosRunHost.IsDescending);
            Assert.True(ChaosRunHost.IsManuallyPaused);
            Assert.True(ChaosRunHost.Hud!.IsVisible);
            shell.HandlePanicKeyPress(DateTime.UtcNow);
            Assert.False(ChaosRunHost.IsDescending);
            Assert.True(ChaosRunHost.IsActive);
            ChaosRunHost.ForceShutdown();
            Start();
            shell.HandlePanicKeyPress(DateTime.UtcNow);
            Assert.True(ChaosRunHost.IsManuallyPaused);
            PanicSurfaces.StopAll("test", shell);
            Assert.False(ChaosRunHost.IsActive);
            Assert.Null(ChaosRunHost.Hud);
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); shell.Close();
            (settings.PanicKeyEnabled, settings.PanicKey, settings.PanicOverridesAll) = (enabled, key, overrides);
        }
    });
}
