using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ChaosFlashPayloadTests
{
    // Startup's handler, the real enabled pool and the screen resolver, rather than ShowPath.
    [Theory]
    [InlineData(false, false, false, 10700, 0.10, 1)]
    [InlineData(true, false, false, 15700, 0.30, 1)]
    [InlineData(false, true, false, 10700, 0.10, 2)]
    [InlineData(false, false, true, 0, 0.0, 0)]
    public async Task FiredPayloadUsesPoolScreensAndWpfTiming(bool glitch, bool dual, bool disabled,
        int endMs, double peak, int windows)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var gif = ChaosFlashOverlayTests.WriteGif(out var dir);
            var images = Directory.CreateDirectory(Path.Combine(dir, "images")).FullName;
            File.Move(gif, Path.Combine(images, "tiny.gif"));
            var schedule = ChaosFlashOverlay.Schedule;
            var assets = CorePaths.EffectiveAssetsProvider;
            var query = ScreenList.QueryOverride;
            var firer = EffectPayload.Firer;
            var global = EffectPayload.GlobalDurationMult;
            var settings = new SettingsService();
            var provider = CoreSettings.ServiceProvider;
            var host = new Window();
            var steps = new List<(Action Run, TimeSpan At)>();
            try
            {
                CoreSettings.ServiceProvider = () => settings;
                settings.Current.DualMonitorEnabled = dual;
                settings.Current.GlobalTargetMonitor = -1;
                if (disabled) settings.Current.DisabledAssetPaths.Add("images/tiny.gif");
                CorePaths.EffectiveAssetsProvider = () => dir;
                ScreenList.QueryOverride = _ => new[]
                {
                    MakeScreen(new PixelRect(1920, 0, 1280, 1024), false),
                    MakeScreen(new PixelRect(0, 0, 1920, 1080), true)
                };
                ChaosFlashOverlay.SkipPlatformChecksForTests = true;
                ChaosFlashOverlay.Schedule = (run, at) => steps.Add((run, at));
                AvApp.InstallChaosPayloads(() => host);
                EffectPayload.GlobalDurationMult = 2;
                new OverlayPayload("braindrain", glitch ? 0.30 : null)
                    { Strength = 50, DurationMult = 2.5 }.Fire();
                Assert.NotNull(ChaosFlashOverlay.LastShow);
                await ChaosFlashOverlay.LastShow!;
                Assert.Equal(windows, ChaosFlashOverlay.Shown.Count);
                if (disabled) Assert.Empty(steps); // empty enabled pool must never fall back to disk
                else
                {
                    Assert.Equal(TimeSpan.FromMilliseconds(endMs), steps[^1].At);
                    foreach (var step in steps.Where(x => x.At.TotalMilliseconds <= 500)) step.Run();
                    Assert.Equal(peak, ChaosFlashOverlay.Alpha, 10);
                    Assert.Equal(1920, ChaosFlashOverlay.Shown[dual ? 1 : 0].Width);
                    PanicSurfaces.StopAll("fired wash");
                    foreach (var step in steps) step.Run();
                    Assert.False(ChaosFlashOverlay.IsShowing);
                }
            }
            finally
            {
                ChaosFlashOverlay.CloseActive();
                host.Close();
                ChaosFlashOverlay.SkipPlatformChecksForTests = false;
                ChaosFlashOverlay.Schedule = schedule;
                CorePaths.EffectiveAssetsProvider = assets;
                CoreSettings.ServiceProvider = provider;
                ScreenList.QueryOverride = query;
                EffectPayload.Firer = firer;
                EffectPayload.GlobalDurationMult = global;
                Directory.Delete(dir, true);
            }
        });
    }

    private static Screen MakeScreen(PixelRect bounds, bool primary)
    {
        // Same synthetic topology as MultiMonitorResultTests, pinned to Avalonia 12.1.
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(bounds.X), "headless-test") })!;
        var type = typeof(Screen);
        type.GetProperty("Scaling")!.SetValue(screen, 1d);
        type.GetProperty("Bounds")!.SetValue(screen, bounds);
        type.GetProperty("WorkingArea")!.SetValue(screen, bounds);
        type.GetProperty("IsPrimary")!.SetValue(screen, primary);
        return screen;
    }
}
