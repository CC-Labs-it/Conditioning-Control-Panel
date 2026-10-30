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
            var oldClock = ChaosGifCascadeOverlay.Clock;
            var oldSkip = ChaosGifCascadeOverlay.SkipPlatformChecksForTests;
            var oldManual = ChaosGifCascadeOverlay.ManualFramesForTests;
            var oldMotion = CoreSettings.Current.MotionLevel;
            try
            {
                ChaosGifCascadeOverlay.CloseActive();
                ChaosGifCascadeOverlay.Clock = clock;
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
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
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                clock.Advance(0.1);
                w.Frame();
                Assert.Same(src, first.Source);
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
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
                ChaosGifCascadeOverlay.Clock = oldClock;
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = oldSkip;
                ChaosGifCascadeOverlay.ManualFramesForTests = oldManual;
                CoreSettings.Current.MotionLevel = oldMotion;
            }
        });
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public Task AFailedAnimatedDecodeGivesItsSlotBack() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var broken = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        File.WriteAllText(broken, "not a gif");
        var oldSkip = ChaosGifCascadeOverlay.SkipPlatformChecksForTests;
        var oldManual = ChaosGifCascadeOverlay.ManualFramesForTests;
        try
        {
            ChaosGifCascadeOverlay.CloseActive();
            ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
            ChaosGifCascadeOverlay.ManualFramesForTests = true;
            Show(broken);
            var w = ChaosGifCascadeOverlay.Active!;
            Assert.Equal(1, w.Cascade.AnimatedAlive);
            await Decoded();
            Assert.Equal(0, w.Cascade.AnimatedAlive);
            Assert.True(w.Cascade.CanAnimate(true, 1));
        }
        finally
        {
            ChaosGifCascadeOverlay.CloseActive();
            ChaosGifCascadeOverlay.SkipPlatformChecksForTests = oldSkip;
            ChaosGifCascadeOverlay.ManualFramesForTests = oldManual;
            File.Delete(broken);
        }
    });

    [Fact]
    public Task TestsRestoreOverridesBeforeTheNextTestRuns() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var motion = CoreSettings.Current.MotionLevel;
        try
        {
            await AFailedAnimatedDecodeGivesItsSlotBack();
            Assert.False(ChaosGifCascadeOverlay.SkipPlatformChecksForTests);
            Assert.False(ChaosGifCascadeOverlay.ManualFramesForTests);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            await RainsSpawnsAnimatesDrainsAndPanicClosesIt();
            Assert.Equal(MotionLevel.Reduced, CoreSettings.Current.MotionLevel);
            Assert.False(ChaosGifCascadeOverlay.SkipPlatformChecksForTests);
            Assert.False(ChaosGifCascadeOverlay.ManualFramesForTests);
        }
        finally { CoreSettings.Current.MotionLevel = motion; }
    });

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
