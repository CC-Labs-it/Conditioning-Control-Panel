using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralGentleWindowFadeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task VideoStopAndRestart(bool gentle) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("spiral-fade-");
        var path = Path.Combine(dir.FullName, "loop.mp4");
        File.WriteAllBytes(path, new byte[] { 1 });
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize, s.GentleVisuals, s.GentleFadeSeconds);
        var start = SpiralOverlay.VideoStart;
        var checks = SpiralOverlay.SkipPlatformChecksForTests;
        var engine = CoreSession.IsEngineRunningProvider;
        var host = new Window();
        int stopped = 0;
        try
        {
            host.Show();
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = true;
            CoreSession.IsEngineRunningProvider = null;
            SpiralOverlay.VideoStart = (_, _) => new OnDispose(() => stopped++);
            (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize, s.GentleVisuals, s.GentleFadeSeconds) = (true, path, false, gentle, 0.25);
            SpiralOverlay.Refresh(host);
            var window = Assert.Single(SpiralOverlay.Shown);
            if (gentle)
            {
                Assert.Equal(0, window.Opacity);
                var transition = Assert.IsType<DoubleTransition>(Assert.Single(window.Transitions!));
                Assert.Equal(TimeSpan.FromSeconds(0.25), transition.Duration);
            }
            else Assert.Equal(1, window.Opacity);
            s.SpiralEnabled = false;
            SpiralOverlay.Refresh(host);
            Assert.Equal(gentle, SpiralOverlay.IsShowing);
            Assert.Equal(gentle ? 0 : 1, stopped);
            if (gentle)
            {
                s.SpiralEnabled = true;
                SpiralOverlay.Refresh(host);
                Assert.Same(window, Assert.Single(SpiralOverlay.Shown));
                await Task.Delay(350);
                Assert.True(SpiralOverlay.IsShowing);
                Assert.Equal(0, stopped);
                s.SpiralEnabled = false;
                SpiralOverlay.Refresh(host);
                await Task.Delay(350);
                Assert.False(SpiralOverlay.IsShowing);
                Assert.Equal(1, stopped);
                s.SpiralEnabled = true;
                SpiralOverlay.Refresh(host);
                SpiralOverlay.CloseAll();
                Assert.False(SpiralOverlay.IsShowing);
                Assert.Equal(2, stopped);
            }
        }
        finally
        {
            SpiralOverlay.CloseAll(); host.Close();
            SpiralOverlay.VideoStart = start;
            SpiralOverlay.SkipPlatformChecksForTests = checks;
            CoreSession.IsEngineRunningProvider = engine;
            (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize, s.GentleVisuals, s.GentleFadeSeconds) = saved;
            CoreSettings.ServiceProvider = provider;
            dir.Delete(true);
        }
    });

    private sealed class OnDispose(Action action) : IDisposable { public void Dispose() => action(); }
}
