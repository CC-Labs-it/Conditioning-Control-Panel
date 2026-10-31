using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
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
    public async Task PayloadFiresEnabledPoolWithWpfDefaultsAndGlobalDuration()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-cascade-payload-").FullName;
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
                var previousRoot = global::ConditioningControlPanel.CorePaths.EffectiveAssetsProvider;
                var previousFirer = EffectPayload.Firer;
                try
                {
                    global::ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = () => dir;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    global::ConditioningControlPanel.Avalonia.App.StartChaosPayloads();
                    EffectPayload.GlobalDurationMult = 2;
                    // This is the same Fire entry used by ChaosRunHost.OnBubblePopped and the fuse.
                    new GifCascadePayload { DurationMult = 9 }.Fire();
                    await ChaosGifCascadeOverlay.LastPick!;
                    Dispatcher.UIThread.RunJobs();
                    var w = Assert.IsType<ChaosGifCascadeOverlay>(ChaosGifCascadeOverlay.Active);
                    Assert.True(ChaosGifCascadeOverlay.IsRaining);
                    Assert.Equal(400, w.Cascade.Size);
                    Assert.Equal(3.6, w.Cascade.FallSpeed);
                    Assert.Equal(0.9, w.Cascade.Opacity);
                    Assert.Equal(0.45, w.Cascade.StartScale);
                    // WPF uses GlobalDurationMult only, not the per-payload DurationMult.
                    w.Cascade.Step(11.9);
                    Assert.True(w.Cascade.Spawning);
                    w.Cascade.Step(0.1);
                    Assert.False(w.Cascade.Spawning);
                    await Decoded();
                }
                finally
                {
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                    ChaosGifCascadeOverlay.ManualFramesForTests = false;
                    global::ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = previousRoot;
                    EffectPayload.Firer = previousFirer;
                    EffectPayload.GlobalDurationMult = 1;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task PanicCancelsPendingPoolPick()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-cascade-panic-").FullName;
            var previousRoot = global::ConditioningControlPanel.CorePaths.EffectiveAssetsProvider;
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "images"));
                File.WriteAllBytes(Path.Combine(dir, "images", "tiny.gif"), Convert.FromBase64String(TinyGif));
                global::ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = () => dir;
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                ChaosGifCascadeOverlay.ManualFramesForTests = true;
                ChaosGifCascadeOverlay.Show(1.67, 6, 400, 3.6, 0.9);
                // The picker cannot attach until this UI callback yields. Panic wins first.
                PanicSurfaces.All.Single(x => x.Id == "gif-cascade").Stop(null);
                await ChaosGifCascadeOverlay.LastPick!;
                Dispatcher.UIThread.RunJobs();
                Assert.Null(ChaosGifCascadeOverlay.Active);
                Assert.False(ChaosGifCascadeOverlay.IsRaining);
            }
            finally
            {
                ChaosGifCascadeOverlay.CloseActive();
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                ChaosGifCascadeOverlay.ManualFramesForTests = false;
                global::ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = previousRoot;
                Directory.Delete(dir, true);
            }
        });
    }

    // WPF ChaosModeService.cs:2295-2319: reserve before selection, drop without queueing.
    [Fact]
    public async Task DetonationsDuringPendingSelectionRespectTheElevenSecondFloor()
    {
        await WithDetonationRun(async () =>
        {
            var now = DateTime.UnixEpoch;
            BubbleOverlay.Field.Clock = () => now;
            Detonate(new GifCascadePayload());
            var pick = ChaosGifCascadeOverlay.LastPick;
            Assert.NotNull(pick);
            Assert.Null(ChaosGifCascadeOverlay.Active); // still in this UI callback, selection cannot attach
            Detonate(new GifCascadePayload());
            Detonate(new VideoPayload());
            Assert.Same(pick, ChaosGifCascadeOverlay.LastPick);
            Assert.Equal(3, ChaosRunHost.State!.Detonated); // only the effects are dropped
            Assert.Contains(Loc.GetF("chaos_heavy_fizzle", "gif cascade"), ChaosRunHost.State.RecentEvents);
            await pick;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ChaosGifCascadeOverlay.IsRaining);
            ChaosGifCascadeOverlay.CloseActive(); // even early completion retains the minimum gate
            now = now.AddSeconds(10.999);
            Detonate(new GifCascadePayload());
            Assert.Same(pick, ChaosGifCascadeOverlay.LastPick);
            now = now.AddMilliseconds(1);
            Assert.False(ChaosRunHost.HeavyEffectActive);
            Detonate(new GifCascadePayload());
            Assert.NotSame(pick, ChaosGifCascadeOverlay.LastPick);
            await ChaosGifCascadeOverlay.LastPick!;
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    public async Task LiveRainBlocksHeavyDetonationsAfterTheEstimatedEnd()
    {
        await WithDetonationRun(async () =>
        {
            var now = DateTime.UnixEpoch;
            BubbleOverlay.Field.Clock = () => now;
            Detonate(new GifCascadePayload());
            var pick = ChaosGifCascadeOverlay.LastPick!;
            await pick;
            Dispatcher.UIThread.RunJobs();
            var w = Assert.IsType<ChaosGifCascadeOverlay>(ChaosGifCascadeOverlay.Active);
            var first = Assert.Single(w.Surface.Children);
            now = now.AddSeconds(12); // floor has expired but clips have not drained
            Assert.True(ChaosRunHost.HeavyEffectActive);
            Detonate(new GifCascadePayload());
            Detonate(new VideoPayload());
            Assert.Same(pick, ChaosGifCascadeOverlay.LastPick);
            Assert.Same(first, Assert.Single(w.Surface.Children)); // no restart or replacement
            Assert.Equal(3, ChaosRunHost.State!.Detonated);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunTeardownClearsTheHeavyReservation(bool normalEnd)
    {
        await WithDetonationRun(async () =>
        {
            BubbleOverlay.Field.Clock = () => DateTime.UnixEpoch;
            Detonate(new GifCascadePayload());
            Assert.True(ChaosRunHost.HeavyEffectActive);
            if (normalEnd) ChaosRunHost.RequestStop();
            else ChaosRunHost.ForceShutdown();
            await ChaosGifCascadeOverlay.LastPick!;
            Dispatcher.UIThread.RunJobs();
            Assert.False(ChaosRunHost.HeavyEffectActive);
            Assert.Null(ChaosGifCascadeOverlay.Active);
        });
    }

    private sealed class GateVideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }

    [Fact]
    public async Task PlayingMandatoryVideoBlocksCascadeDetonation()
    {
        await WithDetonationRun(() =>
        {
            var previousVideo = CoreEngine.Video;
            var video = new MandatoryVideoScheduler(new GateVideoHost());
            try
            {
                CoreEngine.Video = video;
                Assert.True(video.Trigger(path: "gate-test.mp4"));
                Assert.True(ChaosRunHost.HeavyEffectActive);
                var pick = ChaosGifCascadeOverlay.LastPick;
                Detonate(new GifCascadePayload());
                Assert.Same(pick, ChaosGifCascadeOverlay.LastPick);
                Assert.Equal(1, ChaosRunHost.State!.Detonated);
            }
            finally { video.ForceCleanup(); CoreEngine.Video = previousVideo; }
            return Task.CompletedTask;
        });
    }

    private static void Detonate(EffectPayload payload) => ChaosRunHost.OnDetonated(new AmbientBubble
    {
        Spec = new EffectBubbleSpec { VariantId = "htlink", IsLive = true, Payload = payload },
    });

    private static async Task WithDetonationRun(Func<Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-cascade-detonation-").FullName;
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
                var previousRoot = CorePaths.EffectiveAssetsProvider;
                var previousFirer = EffectPayload.Firer;
                var previousClock = BubbleOverlay.Field.Clock;
                try
                {
                    ChaosRunHost.ForceShutdown();
                    ChaosGifCascadeOverlay.CloseActive();
                    CorePaths.EffectiveAssetsProvider = () => dir;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    global::ConditioningControlPanel.Avalonia.App.StartChaosPayloads();
                    ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                    ChaosRunHost.Overlay!.FinishCountdown();
                    ChaosRunHost.StopTimerForTests();
                    await body();
                }
                finally
                {
                    ChaosRunHost.ForceShutdown();
                    ChaosGifCascadeOverlay.CloseActive();
                    if (ChaosGifCascadeOverlay.LastDecode is { } decode) await decode;
                    Dispatcher.UIThread.RunJobs();
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                    ChaosGifCascadeOverlay.ManualFramesForTests = false;
                    CorePaths.EffectiveAssetsProvider = previousRoot;
                    EffectPayload.Firer = previousFirer;
                    BubbleOverlay.Field.Clock = previousClock;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
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
