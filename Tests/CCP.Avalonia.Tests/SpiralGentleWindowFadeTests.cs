using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
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
        var callbacks = new List<Action<Bitmap>>();
        using var firstFrame = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96));
        using var nextFrame = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96));
        try
        {
            host.Show();
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = true;
            CoreSession.IsEngineRunningProvider = null;
            SpiralOverlay.VideoStart = (_, frame) => { callbacks.Add(frame); return new OnDispose(() => stopped++); };
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
            Assert.False(SpiralOverlay.IsShowing);
            Assert.Equal(gentle, window.IsVisible);
            Assert.Equal(gentle ? 0 : 1, stopped);
            if (gentle)
            {
                s.SpiralEnabled = true;
                SpiralOverlay.Refresh(host);
                var replacement = Assert.Single(SpiralOverlay.Shown);
                Assert.NotSame(window, replacement);
                Assert.True(window.IsVisible);
                Assert.True(SpiralOverlay.IsVideoPlaying);
                Assert.Equal(2, callbacks.Count);
                callbacks[0](firstFrame);
                callbacks[1](nextFrame);
                Assert.Same(firstFrame, window.Spiral.Source);
                Assert.Same(nextFrame, replacement.Spiral.Source);
                await Task.Delay(350);
                Assert.False(window.IsVisible);
                Assert.True(replacement.IsVisible);
                Assert.True(SpiralOverlay.IsShowing);
                Assert.True(SpiralOverlay.IsVideoPlaying);
                Assert.Equal(1, stopped);
                s.SpiralEnabled = false;
                SpiralOverlay.Refresh(host);
                await Task.Delay(350);
                Assert.False(SpiralOverlay.IsShowing);
                Assert.False(replacement.IsVisible);
                Assert.Equal(2, stopped);
                s.SpiralEnabled = true;
                SpiralOverlay.Refresh(host);
                var fading = Assert.Single(SpiralOverlay.Shown);
                s.SpiralEnabled = false;
                SpiralOverlay.Refresh(host);
                s.SpiralEnabled = true;
                SpiralOverlay.Refresh(host);
                var active = Assert.Single(SpiralOverlay.Shown);
                Assert.True(fading.IsVisible);
                SpiralOverlay.CloseAll();
                Assert.False(fading.IsVisible);
                Assert.False(active.IsVisible);
                Assert.False(SpiralOverlay.IsShowing);
                Assert.Equal(4, stopped);
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

    // Three distinct frames, so the stopped image can be checked while the fade is visible.
    private const string TinyGif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    [Fact]
    public Task GifStopFreezesLastFrameWhileOldWindowFades() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("spiral-gif-fade-");
        var path = Path.Combine(dir.FullName, "loop.gif");
        File.WriteAllBytes(path, Convert.FromBase64String(TinyGif));
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var checks = SpiralOverlay.SkipPlatformChecksForTests;
        var engine = CoreSession.IsEngineRunningProvider;
        var host = new Window();
        try
        {
            host.Show();
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = true;
            CoreSession.IsEngineRunningProvider = null;
            (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize, s.GentleVisuals, s.GentleFadeSeconds) = (true, path, false, true, 0.5);
            SpiralOverlay.Refresh(host);
            if (SpiralOverlay.Decoding is { } decode) await decode;
            Assert.True(SpiralOverlay.IsAnimating);
            SpiralOverlay.Tick();
            var old = Assert.Single(SpiralOverlay.Shown);
            var lastFrame = Assert.IsType<Bitmap>(old.Spiral.Source);
            s.SpiralEnabled = false;
            SpiralOverlay.Refresh(host);
            Assert.False(SpiralOverlay.IsAnimating);
            Assert.False(SpiralOverlay.IsShowing);
            Assert.True(old.IsVisible);
            await Task.Delay(150);
            Assert.Same(lastFrame, old.Spiral.Source);
            SpiralOverlay.Refresh(host); // a repeated stop must not cut the fading window
            Assert.True(old.IsVisible);
            s.SpiralEnabled = true;
            SpiralOverlay.Refresh(host);
            if (SpiralOverlay.Decoding is { } restart) await restart;
            var current = Assert.Single(SpiralOverlay.Shown);
            Assert.NotSame(old, current);
            Assert.True(old.IsVisible);
            Assert.Same(lastFrame, old.Spiral.Source);
            await Task.Delay(600);
            Assert.False(old.IsVisible);
            Assert.True(current.IsVisible);
            Assert.True(SpiralOverlay.IsAnimating);
        }
        finally
        {
            SpiralOverlay.CloseAll(); host.Close();
            SpiralOverlay.SkipPlatformChecksForTests = checks;
            CoreSession.IsEngineRunningProvider = engine;
            CoreSettings.ServiceProvider = provider;
            dir.Delete(true);
        }
    });

    private sealed class OnDispose(Action action) : IDisposable { public void Dispose() => action(); }
}
