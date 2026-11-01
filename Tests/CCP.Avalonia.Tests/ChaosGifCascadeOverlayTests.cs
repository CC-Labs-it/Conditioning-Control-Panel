using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>chaos-gif-cascade: the WPF ChaosGifCascadeOverlay rain on a stepped clock: one clip at once,
/// a second after the spawn interval, an animated gif advancing frames, the rain draining and arming the
/// hide grace, a restart reusing the window, and the panic registry closing it.</summary>
public sealed class ChaosGifCascadeOverlayTests
{
    // 4x4, three frames, 30 ms each (same GIF as CornerGifOverlayTests).
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = TimeSpan.FromSeconds(10).Ticks;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(double sec) => Now += TimeSpan.FromSeconds(sec).Ticks;
    }

    [Fact]
    public async Task RainsSpawnsAnimatesDrainsAndPanicClosesIt()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-gifcascade-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = new SteppedClock();
            var motion = CoreSettings.Current.MotionLevel;
            try
            {
                ChaosGifCascadeOverlay.CloseActive();
                ChaosGifCascadeOverlay.Clock = clock;
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                Show(gif);                                   // headless: no X11 compositor -> refuse, no window
                Assert.Null(ChaosGifCascadeOverlay.Active);
                Assert.True(ChaosGifCascadeOverlay.Refused);
                ChaosGifCascadeOverlay.CloseActive();        // clears Refused
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                ChaosGifCascadeOverlay.ManualFramesForTests = true;
                Show(gif);

                var w = ChaosGifCascadeOverlay.Active!;
                Assert.NotNull(w);
                Assert.Equal(TimeSpan.Zero, w.FrameInterval);  // every composed frame, as WPF Rendering
                Assert.Equal(w.DesktopScaling, w.Scale);       // the factor SetOverrideRedirect sizes with
                Assert.True(w.IsVisible);
                Assert.True(ChaosGifCascadeOverlay.IsRaining);
                var first = Assert.IsType<Image>(Assert.Single(w.Surface.Children));
                Assert.Equal(0.9, first.Opacity, 3);
                Assert.Equal(400, first.Width);
                await Decoded();
                Assert.NotNull(first.Source);
                Assert.True(w.Cascade.Fallers[0].Animated);   // a small gif inside the animated budget

                var src = first.Source;
                // WPF keeps local GIFs animating at Motion Off; only remote clips freeze.
                clock.Advance(0.1);
                w.Frame();
                Assert.NotSame(src, first.Source);
                src = first.Source;
                clock.Advance(0.6);                          // past the 598.8 ms spawn interval
                w.Frame();
                Assert.Equal(2, w.Surface.Children.Count);
                Assert.NotSame(src, first.Source);           // the gif stepped a frame

                for (int i = 0; i < 400 && ChaosGifCascadeOverlay.IsRaining; i++) { clock.Advance(0.1); w.Frame(); }
                Assert.False(ChaosGifCascadeOverlay.IsRaining);
                Assert.Empty(w.Surface.Children);
                Assert.True(w.IsHideGraceArmed);             // WPF keeps the empty window up 8 s, then hides

                Show(gif);                                   // a follow-up cascade reuses the window
                Assert.Same(w, ChaosGifCascadeOverlay.Active);
                Assert.False(w.IsHideGraceArmed);
                Assert.Single(w.Surface.Children);

                PanicSurfaces.All.Single(x => x.Id == "gif-cascade").Stop(null);
                Assert.Null(ChaosGifCascadeOverlay.Active);
                Assert.False(ChaosGifCascadeOverlay.IsRaining);
                Assert.False(w.IsVisible);
            }
            finally
            {
                ChaosGifCascadeOverlay.CloseActive();
                ChaosGifCascadeOverlay.Clock = TimeProvider.System;
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                ChaosGifCascadeOverlay.ManualFramesForTests = false;
                CoreSettings.Current.MotionLevel = motion;
            }
        });
        try { Directory.Delete(dir, true); } catch { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task EligibleLocalGifKeepsItsSlotUntilDespawn(bool failedDecode) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var skip = ChaosGifCascadeOverlay.SkipPlatformChecksForTests;
        var manual = ChaosGifCascadeOverlay.ManualFramesForTests;
        var previousClock = ChaosGifCascadeOverlay.Clock;
        var clock = new SteppedClock();
        var dir = Directory.CreateTempSubdirectory("ccp-gif-budget-").FullName;
        var gif = Path.Combine(dir, "local.gif");
        // A single-frame GIF still qualifies for a slot, like a local GIF that fails to decode.
        const string oneFrameGif = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";
        try
        {
            File.WriteAllBytes(gif, failedDecode ? System.Text.Encoding.ASCII.GetBytes("not a gif") : Convert.FromBase64String(oneFrameGif));
            ChaosGifCascadeOverlay.CloseActive();
            ChaosGifCascadeOverlay.Clock = clock;
            ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
            ChaosGifCascadeOverlay.ManualFramesForTests = true;
            Show(gif);
            var w = ChaosGifCascadeOverlay.Active!;
            for (var i = 0; i < GifCascade.MaxAnimated; i++)
            {
                if (i > 0) { clock.Advance(0.6); w.Frame(); }
                Assert.Equal(i + 1, w.Cascade.Fallers.Count);
                await Decoded();
                Assert.True(w.Cascade.Fallers[i].Animated);
                Assert.Equal(i + 1, w.Cascade.AnimatedAlive);
                var image = Assert.IsType<Image>(w.Surface.Children[i]);
                if (failedDecode) Assert.Null(image.Source); else Assert.NotNull(image.Source);
            }

            // Three reservations make the next valid animated GIF fall as a still.
            File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
            clock.Advance(0.6);
            w.Frame();
            var capped = w.Cascade.Fallers[3];
            Assert.False(capped.Animated);
            Assert.Equal(GifCascade.MaxAnimated, w.Cascade.AnimatedAlive);
            await Decoded();
            var still = Assert.IsType<Image>(w.Surface.Children[3]);
            var stillSource = Assert.IsAssignableFrom<global::Avalonia.Media.IImage>(still.Source);
            clock.Advance(0.1);
            w.Frame();
            Assert.Same(stillSource, still.Source);

            // A slot comes back when the original faller leaves, so the next GIF animates.
            var first = w.Cascade.Fallers[0];
            first.Y = w.Height + w.Cascade.Size + 1;
            clock.Advance(0.1);
            w.Frame();
            Assert.DoesNotContain(first, w.Cascade.Fallers);
            Assert.Equal(GifCascade.MaxAnimated - 1, w.Cascade.AnimatedAlive);
            clock.Advance(0.6);
            w.Frame();
            Assert.True(w.Cascade.Fallers[^1].Animated);
            Assert.Equal(GifCascade.MaxAnimated, w.Cascade.AnimatedAlive);
            await Decoded();
            var animated = Assert.IsType<Image>(w.Surface.Children[^1]);
            var animatedSource = Assert.IsAssignableFrom<global::Avalonia.Media.IImage>(animated.Source);
            clock.Advance(0.1);
            w.Frame();
            Assert.NotSame(animatedSource, animated.Source);
            Assert.Same(stillSource, still.Source);
        }
        finally
        {
            ChaosGifCascadeOverlay.CloseActive();
            ChaosGifCascadeOverlay.Clock = previousClock;
            ChaosGifCascadeOverlay.SkipPlatformChecksForTests = skip;
            ChaosGifCascadeOverlay.ManualFramesForTests = manual;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public async Task CascadeTestsRestoreStateBeforeTheNextTest()
    {
        var skip = ChaosGifCascadeOverlay.SkipPlatformChecksForTests;
        var manual = ChaosGifCascadeOverlay.ManualFramesForTests;
        var motion = CoreSettings.Current.MotionLevel;
        try
        {
            ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
            ChaosGifCascadeOverlay.ManualFramesForTests = false;
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            // Direct calls keep both cases inside one isolation hook, exposing order-dependent leaks.
            await EligibleLocalGifKeepsItsSlotUntilDespawn(failedDecode: true);
            Assert.False(ChaosGifCascadeOverlay.SkipPlatformChecksForTests);
            Assert.False(ChaosGifCascadeOverlay.ManualFramesForTests);
            await RainsSpawnsAnimatesDrainsAndPanicClosesIt();
            Assert.False(ChaosGifCascadeOverlay.SkipPlatformChecksForTests);
            Assert.False(ChaosGifCascadeOverlay.ManualFramesForTests);
            Assert.Equal(MotionLevel.Reduced, CoreSettings.Current.MotionLevel);
        }
        finally
        {
            ChaosGifCascadeOverlay.SkipPlatformChecksForTests = skip;
            ChaosGifCascadeOverlay.ManualFramesForTests = manual;
            CoreSettings.Current.MotionLevel = motion;
        }
    }

    private static void Show(string gif) => ChaosGifCascadeOverlay.ShowWithFiles(new() { gif },
        GifCascade.PayloadSpawnRatePerSec, GifCascade.PayloadDurationSec, GifCascade.PayloadGifSize,
        GifCascade.PayloadFallSpeed, GifCascade.PayloadOpacity, GifCascade.PayloadStartScale);

    /// <summary>The decode runs on the thread pool; await it, then run the attach it posted.</summary>
    private static async Task Decoded()
    {
        if (ChaosGifCascadeOverlay.LastDecode is { } d) await d;
        Dispatcher.UIThread.RunJobs();
    }
}
