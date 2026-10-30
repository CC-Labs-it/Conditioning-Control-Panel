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
using ConditioningControlPanel.Avalonia.Views.Overlays;
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
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = new SteppedClock();
            try
            {
                ChaosGifCascadeOverlay.CloseActive();
                ChaosGifCascadeOverlay.Clock = clock;
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
            }
        });
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public async Task Live_bubble_detonation_fires_payload_with_global_duration_and_panic_cancels_pending_pick()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rain-path-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "images"));
        File.WriteAllBytes(Path.Combine(dir, "images", "tiny.gif"), Convert.FromBase64String(TinyGif));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var clock = new SteppedClock();
                try
                {
                    CorePaths.EffectiveAssetsProvider = () => dir;
                    EffectPayload.Firer = global::ConditioningControlPanel.Avalonia.App.FireChaosPayload;
                    EffectPayload.GlobalDurationMult = 2;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    ChaosGifCascadeOverlay.Clock = clock;
                    ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                    ChaosRunHost.Overlay!.FinishCountdown();
                    ChaosRunHost.StopTimerForTests();
                    var bubble = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    {
                        VariantId = "htlink", IsLive = true, FuseMs = 5000, SizePx = 200,
                        Payload = new GifCascadePayload { DurationMult = 9 }
                    });
                    // A real failed hold: no focus detonates on press, through the run callback.
                    ChaosRunHost.State!.Focus = 0;
                    BubbleOverlay.Press(bubble, bubble.CenterX, bubble.CenterY);
                    Assert.NotNull(ChaosGifCascadeOverlay.LastPick);
                    await ChaosGifCascadeOverlay.LastPick!;
                    Dispatcher.UIThread.RunJobs();
                    var w = ChaosGifCascadeOverlay.Active!;
                    Assert.NotNull(w);
                    Assert.True(ChaosGifCascadeOverlay.IsRaining);
                    var image = Assert.IsType<Image>(Assert.Single(w.Surface.Children));
                    Assert.Equal(GifCascadePayload.GIF_SIZE, image.Width);
                    Assert.Equal(GifCascadePayload.OPACITY, image.Opacity);
                    Assert.Equal(GifCascadePayload.START_SCALE, w.Cascade.StartScale);
                    Assert.Equal(GifCascadePayload.FALL_SPEED, w.Cascade.FallSpeed);
                    await Decoded();
                    Assert.NotNull(image.Source);
                    // 6 * global 2 = 12 seconds, ignoring the per-instance multiplier as WPF does.
                    for (int i = 0; i < 61; i++) { clock.Advance(0.1); w.Frame(); }
                    Assert.True(w.Cascade.Spawning);
                    for (int i = 0; i < 60; i++) { clock.Advance(0.1); w.Frame(); }
                    Assert.False(w.Cascade.Spawning);
                    Assert.NotEmpty(w.Cascade.Fallers);
                    ChaosRunHost.ForceShutdown();
                    Assert.Null(ChaosGifCascadeOverlay.Active);

                    // End/panic must also invalidate a pool pick whose UI continuation has not run.
                    new GifCascadePayload().Fire();
                    var pending = ChaosGifCascadeOverlay.LastPick!;
                    PanicSurfaces.All.Single(x => x.Id == "gif-cascade").Stop(null);
                    await pending;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Null(ChaosGifCascadeOverlay.Active);
                }
                finally
                {
                    ChaosRunHost.ForceShutdown();
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.Clock = TimeProvider.System;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                    ChaosGifCascadeOverlay.ManualFramesForTests = false;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Heavy_detonations_are_dropped_during_pick_grace_and_real_rain_then_reset_on_shutdown()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var now = DateTime.UtcNow;
            BubbleOverlay.Field.Clock = () => now;
            int fired = 0;
            EffectPayload.Firer = _ => fired++;
            try
            {
                ChaosRunHost.ForceShutdown();
                ChaosRunHost.FirePayloadForDetonation(new GifCascadePayload());
                ChaosRunHost.FirePayloadForDetonation(new GifCascadePayload());
                ChaosRunHost.FirePayloadForDetonation(new VideoPayload());
                Assert.Equal(1, fired);
                ChaosRunHost.FirePayloadForDetonation(new FlashPayload());
                Assert.Equal(2, fired); // light effects still fire
                now = now.AddSeconds(10.9);
                ChaosRunHost.FirePayloadForDetonation(new GifCascadePayload());
                Assert.Equal(2, fired);
                now = now.AddSeconds(0.2);
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                ChaosGifCascadeOverlay.ManualFramesForTests = true;
                Show("missing.gif"); // decoding may fail, but the faller is still real rain
                ChaosRunHost.FirePayloadForDetonation(new GifCascadePayload());
                Assert.Equal(2, fired);
                ChaosGifCascadeOverlay.CloseActive();
                ChaosRunHost.FirePayloadForDetonation(new GifCascadePayload());
                Assert.Equal(3, fired);
                ChaosRunHost.ForceShutdown();
                ChaosRunHost.FirePayloadForDetonation(new GifCascadePayload());
                Assert.Equal(4, fired);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                ChaosGifCascadeOverlay.ManualFramesForTests = false;
            }
            return Task.CompletedTask;
        });
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
