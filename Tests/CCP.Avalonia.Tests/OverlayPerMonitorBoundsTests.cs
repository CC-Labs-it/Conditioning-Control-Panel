using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class OverlayPerMonitorBoundsTests
{
    [Fact]
    public Task BoundsUseTargetScreenScale() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = ScreenList.QueryOverride;
        var bounds = new PixelRect(800, 0, 1200, 900);
        ScreenList.QueryOverride = _ => new[] {
            MultiMonitorResultTests.MakeScreen(1, new PixelRect(0, 0, 800, 600), new PixelRect(0, 0, 800, 600), true),
            MultiMonitorResultTests.MakeScreen(1.5, bounds, bounds, false) };
        var window = new Window();
        try
        {
            X11Overlay.SetOverrideRedirect(window, bounds);
            Assert.Equal(800, window.Width);
            Assert.Equal(600, window.Height);
        }
        finally { window.Close(); ScreenList.QueryOverride = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TintReplacesChangedGeometryButReusesForOpacity() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var (skip, query, hold) = (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold);
        var host = new Window();
        var bounds = new PixelRect(0, 0, 800, 600);
        var scale = 1.0;
        try
        {
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.PulseHold = true;
            ScreenList.QueryOverride = _ => new[] { MultiMonitorResultTests.MakeScreen(scale, bounds, bounds, true) };
            settings.Current.PinkFilterEnabled = true;
            settings.Current.GentleVisuals = false;
            host.Show();
            PinkFilterOverlay.Refresh(host);
            var first = Assert.Single(PinkFilterOverlay.Shown);
            settings.Current.PinkFilterOpacity = 30;
            PinkFilterOverlay.Refresh(host);
            Assert.Same(first, Assert.Single(PinkFilterOverlay.Shown));
            bounds = new PixelRect(20, 30, 900, 750);
            typeof(PinkFilterOverlay).GetMethod("OnScreensChanged", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { host.Screens, EventArgs.Empty });
            var second = Assert.Single(PinkFilterOverlay.Shown);
            Assert.NotSame(first, second);
            Assert.False(first.IsVisible);
            Assert.Equal(bounds.Position, second.Position);
            Assert.Equal(900, second.Width);
            scale = 1.5;
            typeof(PinkFilterOverlay).GetMethod("OnScreensChanged", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { host.Screens, EventArgs.Empty });
            var third = Assert.Single(PinkFilterOverlay.Shown);
            Assert.NotSame(second, third);
            Assert.False(second.IsVisible);
            Assert.Equal(600, third.Width);
            Assert.Equal(500, third.Height);
        }
        finally
        {
            PinkFilterOverlay.Stop(); host.Close();
            (PinkFilterOverlay.SkipPlatformChecksForTests, ScreenList.QueryOverride, PinkFilterOverlay.PulseHold) = (skip, query, hold);
            CoreSettings.ServiceProvider = provider;
        }
        return Task.CompletedTask;
    });
}
