using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF FlashService.cs:1773-1778,3202,3258,3331,4854-4866.
public sealed class FlashGentleRenderingTests
{
    // Existing SpiralOverlayTests fixture: red, green and blue, 30 ms each.
    private const string Gif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => 1000;
    }

    private static Task OnUi(Func<Clock, Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new Clock();
        FlashOverlay.Clock = clock;
        FlashOverlay.Present = (_, _, _) => true;
        CoreSettings.Current.FlashMotionStyle = FlashMotionStyle.Still;
        try { await test(clock); }
        finally { FlashOverlay.CloseAll(final: false); }
    });

    [Theory]
    [InlineData(true, 0.25, 0.25, 0.2)]
    [InlineData(true, 4, 0.125, 0.1)] // never shorten an already slower ramp
    [InlineData(false, 0.25, 1, 0.95)]
    public Task FadeFloorReachesTheNativeOpacityRamp(bool gentle, double normal, double expected, double afterExit) => OnUi(clock =>
    {
        var s = CoreSettings.Current;
        (s.GentleVisuals, s.GentleFadeSeconds) = (gentle, 2);
        var w = new FlashOverlayWindow();
        try
        {
            w.Run(1, TimeSpan.FromSeconds(normal), TimeSpan.FromMinutes(1));
            clock.Now += 500; w.FadeFrame();
            Assert.Equal(expected, w.CurrentOpacity, 6);
            s.GentleVisuals = true;
            w.Expire();
            clock.Now += 100; w.FadeFrame();
            Assert.Equal(afterExit, w.CurrentOpacity, 6);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LiveFadeEditAndGentlePopUseTheRampAndForceCloseStaysImmediate() => OnUi(clock =>
    {
        var s = CoreSettings.Current;
        (s.GentleVisuals, s.GentleFadeSeconds, s.FlashShatterEnabled, s.CorruptionMode) = (true, 2, true, false);
        s.FlashExitStyle = FlashExitStyle.Glitch;
        var host = new Window();
        try
        {
            var picture = new Bitmap(global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));
            Assert.True(FlashOverlay.Show(picture, new PixelRect(0, 0, 100, 100), "still.png", host.Screens.All[0],
                1, TimeSpan.Zero, TimeSpan.FromMinutes(1), true, false, 0));
            var w = FlashOverlay.Active[^1].Window;
            clock.Now += 500; w.FadeFrame(); Assert.Equal(0.25, w.CurrentOpacity);
            s.GentleFadeSeconds = 1;
            clock.Now += 750; w.FadeFrame(); Assert.Equal(1, w.CurrentOpacity);
            FlashOverlay.Pop(w);
            Assert.True(w.IsFadingOut);
            Assert.Null(w.Exit); Assert.Null(w.Shatter);
            clock.Now += 250; w.FadeFrame(); Assert.Equal(0.75, w.CurrentOpacity);
            FlashOverlay.CloseAll(final: false);
            Assert.False(w.IsVisible);
            Assert.Null(w.Picture.Source);
            clock.Now += 1000; w.FadeFrame(); Assert.Equal(0.75, w.CurrentOpacity);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 1, 30)]
    [InlineData(true, 1, 120)]
    [InlineData(true, 2, 60)]
    public Task ActualGifFramesUseTheGentleDelayAndStopOnClose(bool gentle, double speed, int delay) => OnUi(async clock =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        File.WriteAllBytes(path, Convert.FromBase64String(Gif));
        var host = new Window();
        try
        {
            var s = CoreSettings.Current;
            (s.GentleVisuals, s.GentleSpeed, s.FlashGifSpeedMultiplier) = (gentle, 0.25, speed);
            Assert.True(FlashOverlay.Show(new Bitmap(path), new PixelRect(0, 0, 4, 4), path, host.Screens.All[0],
                1, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, false, 0));
            var w = FlashOverlay.Active[^1].Window;
            await w.AnimationLoad;
            Assert.Equal(TimeSpan.FromMilliseconds(delay), w.FrameDelay);
            var first = w.Picture.Source;
            clock.Now += delay - 1; w.AnimationFrame(); Assert.Same(first, w.Picture.Source);
            clock.Now++; w.AnimationFrame(); Assert.NotSame(first, w.Picture.Source);
            FlashOverlay.CloseAll(final: false);
            Assert.Null(w.Picture.Source);
            clock.Now += delay; w.AnimationFrame(); Assert.Null(w.Picture.Source);
        }
        finally { host.Close(); File.Delete(path); }
    });
}
