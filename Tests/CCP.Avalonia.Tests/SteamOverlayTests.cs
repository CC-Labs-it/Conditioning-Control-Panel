using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Super Steam on this head: the fog rides the pink filter's tint windows.</summary>
public class SteamOverlayTests
{
    private static global::Avalonia.Platform.Screen Screen()
    {
        var s = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(s, 1.0);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(s, true);
        return s;
    }

    [Fact]
    public Task TheStageSwitchSwapsTheTintForTheFogAndBack() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        var (skip, query, hold) = (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold);
        var host = new Window();
        try
        {
            SteamOverlay.Seed();
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.PulseHold = true;
            ScreenList.QueryOverride = _ => new[] { Screen() };
            s.PinkFilterEnabled = true;
            s.GentleVisuals = false;
            host.Show();
            PinkFilterOverlay.Refresh(host);
            var tint = Assert.Single(PinkFilterOverlay.Shown);
            // No Super access in a test profile: the tint stays the flat wash.
            Assert.IsType<Border>(tint.Content);
            Assert.False(SteamOverlay.On);

            SuperStageHost.SteamSurface(true);
            Assert.True(SteamOverlay.On);
            var fog = Assert.IsType<SteamCanvas>(tint.Content);

            // The picture is Core's; here it only has to draw a monitor's field without throwing.
            SuperStageHost.Stage.SteamOn = true;
            using (var surface = SKSurface.Create(new SKImageInfo(800, 600)))
            {
                fog.Draw(surface.Canvas);
                fog.Draw(surface.Canvas);
            }

            SuperStageHost.SteamSurface(false);
            Assert.False(SteamOverlay.On);
            Assert.IsType<Border>(tint.Content);

            // Closing the tint tells the stage, and the stage finds no surface to keep the fog on.
            SuperStageHost.SteamSurface(true);
            PinkFilterOverlay.Stop();
            Assert.False(SteamOverlay.On);
            Assert.False(SuperStageHost.Stage.SteamOn);
        }
        finally
        {
            SuperStageHost.Stop();
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.SkipPlatformChecksForTests = skip;
            PinkFilterOverlay.PulseHold = hold;
            ScreenList.QueryOverride = query;
            host.Close();
            CoreSettings.ServiceProvider = provider;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void TheSeedHandsTheMotionLevelAndTheBundledFace() => AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            SteamOverlay.Seed();
            var s = settings.Current;
            s.GentleVisuals = false;
            s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            Assert.Equal(ConditioningControlPanel.Models.MotionLevel.Off, SuperStepAside.LevelProvider());
            s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            s.GentleVisuals = true;   // Gentle visuals caps the level, as WPF MotionFx.Level
            Assert.Equal(ConditioningControlPanel.Models.MotionLevel.Reduced, SuperStepAside.LevelProvider());

            Assert.NotNull(ConditioningControlPanel.Services.Compositor.SubliminalLayer.BundledFaceStream);
            using (var face = SteamOverlay.BundledFace("Fredoka (bundled)")) Assert.NotNull(face);
            Assert.Null(SteamOverlay.BundledFace("Arial"));
        }
        finally { CoreSettings.ServiceProvider = provider; }
    });
}
