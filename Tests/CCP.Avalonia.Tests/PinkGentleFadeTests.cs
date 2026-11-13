using System;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PinkGentleFadeTests
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

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public Task TintFadesOnlyOnGentleLegacyPath(bool gentle, bool unified, bool fades) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        var saved = (s.PinkFilterEnabled, s.GentleVisuals, s.GentleFadeSeconds, s.UnifiedOverlayHost);
        var (skip, query, hold) = (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold);
        var host = new Window();
        try
        {
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.PulseHold = true;
            ScreenList.QueryOverride = _ => new[] { Screen() };
            (s.PinkFilterEnabled, s.GentleVisuals, s.GentleFadeSeconds, s.UnifiedOverlayHost) = (true, gentle, 0.5, unified);
            host.Show();
            PinkFilterOverlay.Refresh(host);
            var tint = Assert.Single(PinkFilterOverlay.Shown);
            Assert.Equal(fades ? 0 : 1, tint.Opacity);
            if (fades)
            {
                await Task.Delay(350);
                Assert.InRange(tint.Opacity, 0.01, 0.99);
            }
            var beforeHide = tint.Opacity;
            s.PinkFilterEnabled = false;
            PinkFilterOverlay.Refresh(host);
            Assert.False(PinkFilterOverlay.IsShowing);
            Assert.Equal(fades, tint.IsVisible);
            if (fades)
            {
                await Task.Delay(200);
                Assert.True(tint.IsVisible);
                Assert.True(tint.Opacity < beforeHide);
                PinkFilterOverlay.Refresh(host); // repeated polling must not cut the fade short
                Assert.True(tint.IsVisible);
                await Task.Delay(450);
                Assert.False(tint.IsVisible);
            }
        }
        finally
        {
            PinkFilterOverlay.Stop(); host.Close();
            (s.PinkFilterEnabled, s.GentleVisuals, s.GentleFadeSeconds, s.UnifiedOverlayHost) = saved;
            (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold) = (skip, query, hold);
            CoreSettings.ServiceProvider = provider;
        }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task StopAndPanicCloseImmediately(bool alreadyFading, bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        var saved = (s.PinkFilterEnabled, s.GentleVisuals, s.UnifiedOverlayHost);
        var (skip, query, hold) = (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold);
        var host = new Window();
        try
        {
            PinkFilterOverlay.Stop(); PinkFilterOverlay.SkipPlatformChecksForTests = true; PinkFilterOverlay.PulseHold = true;
            ScreenList.QueryOverride = _ => new[] { Screen() };
            (s.PinkFilterEnabled, s.GentleVisuals, s.UnifiedOverlayHost) = (true, true, false);
            host.Show(); PinkFilterOverlay.Refresh(host);
            var tint = Assert.Single(PinkFilterOverlay.Shown);
            if (alreadyFading) { s.PinkFilterEnabled = false; PinkFilterOverlay.Refresh(host); }
            Assert.True(tint.IsVisible);
            if (panic) PanicSurfaces.All.Single(x => x.Id == "voice-overlays").Stop(null);
            else PinkFilterOverlay.Stop();
            Assert.False(tint.IsVisible);
        }
        finally
        {
            PinkFilterOverlay.Stop(); host.Close();
            (s.PinkFilterEnabled, s.GentleVisuals, s.UnifiedOverlayHost) = saved;
            (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold) = (skip, query, hold);
            CoreSettings.ServiceProvider = provider;
        }
        return Task.CompletedTask;
    });
}
