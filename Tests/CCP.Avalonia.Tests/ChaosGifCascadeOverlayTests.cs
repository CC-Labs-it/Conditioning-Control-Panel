using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    public async Task StillWebpsLeaveAnimationSlotsForAnAnimatedWebp()
    {
        // Valid 2x2 lossless WebPs, one still and one with two 100 ms frames.
        const string stillBytes = "UklGRhwAAABXRUJQVlA4TA8AAAAvAUAAAAcQ/Y/+ByKi/wEA";
        const string animatedBytes = "UklGRoQAAABXRUJQVlA4WAoAAAACAAAAAQAAAQAAQU5JTQYAAAAAAAAAAABBTk1GKAAAAAAAAAAAAAEAAAEAAGQAAAJWUDhMDwAAAC8BQAAABxD9j/4HIqL/AQBBTk1GKAAAAAAAAAAAAAEAAAEAAGQAAABWUDhMDwAAAC8BQAAABxDR//4HIqL/AQA=";
        var dir = Directory.CreateTempSubdirectory("ccp-cascade-webp-").FullName;
        var still = Path.Combine(dir, "still.webp");
        var animated = Path.Combine(dir, "animated.WEBP");
        var truncated = Path.Combine(dir, "truncated.webp");
        File.WriteAllBytes(still, Convert.FromBase64String(stillBytes));
        File.WriteAllBytes(animated, Convert.FromBase64String(animatedBytes));
        File.WriteAllBytes(truncated, Convert.FromBase64String(animatedBytes).Take(20).ToArray());
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var previousClock = ChaosGifCascadeOverlay.Clock;
                var previousSkip = ChaosGifCascadeOverlay.SkipPlatformChecksForTests;
                var previousManual = ChaosGifCascadeOverlay.ManualFramesForTests;
                var clock = new SteppedClock();
                try
                {
                    ChaosGifCascadeOverlay.Clock = clock;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    var paths = new System.Collections.Generic.List<string> { still };
                    ChaosGifCascadeOverlay.ShowWithFiles(paths, 1, 10, 400, 3.6, 0.9, 0.45);
                    var window = Assert.IsType<ChaosGifCascadeOverlay>(ChaosGifCascadeOverlay.Active);
                    await Decoded();
                    for (int i = 0; i < 2; i++) { window.Cascade.Step(1); await Decoded(); }
                    Assert.Equal(3, window.Cascade.Fallers.Count);
                    Assert.All(window.Cascade.Fallers, f => Assert.False(f.Animated));
                    Assert.Equal(0, window.Cascade.AnimatedAlive);
                    Assert.All(window.Surface.Children, child => Assert.NotNull(Assert.IsType<Image>(child).Source));

                    paths[0] = animated;
                    window.Cascade.Step(1);
                    await Decoded();
                    Assert.True(window.Cascade.Fallers.Last().Animated);
                    Assert.Equal(1, window.Cascade.AnimatedAlive);
                    var image = Assert.IsType<Image>(window.Surface.Children.Last());
                    var first = Assert.IsAssignableFrom<global::Avalonia.Media.IImage>(image.Source);
                    clock.Advance(0.12);
                    window.Frame();
                    Assert.NotSame(first, image.Source);
                    paths[0] = truncated;
                    window.Cascade.Step(1);
                    await Decoded();
                    Assert.False(window.Cascade.Fallers.Last().Animated);
                    Assert.Equal(1, window.Cascade.AnimatedAlive);
                }
                finally
                {
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.Clock = previousClock;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = previousSkip;
                    ChaosGifCascadeOverlay.ManualFramesForTests = previousManual;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(4, 40, 400, 60)]       // 120 frames, 96-frame ceiling samples every second frame
    [InlineData(400, 40, 400, 30)]   // 24 MiB at 400x400 samples every fourth frame
    [InlineData(4, 340, 400, 55)]    // 1020 frames, but decode stops after the first 600
    [InlineData(1290, 40, 100, 60)]  // budget uses display size, not the larger source
    public async Task AnimatedCascadeDecodeRespectsWpfFrameAndMemoryBudgets(int size, int repeats, int pixels, int expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-cascade-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, BuildGif(size, repeats));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var (frames, _) = ChaosGifCascadeOverlay.Decode(path, pixels, true);
                try { Assert.Equal(expected, frames.Count); }
                finally { foreach (var frame in frames) frame.Dispose(); }
                return Task.CompletedTask;
            });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OversizedAnimatedCascadeFallsBackToOneStill()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-cascade-large-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, BuildGif(4097, 1));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var (frames, _) = ChaosGifCascadeOverlay.Decode(path, 400, true);
                try { Assert.Single(frames); }
                finally { foreach (var frame in frames) frame.Dispose(); }
                return Task.CompletedTask;
            });
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1, 3, 9, 0, 60)]
    [InlineData(40, 3, 9, 0, 120)] // 120 frames, every second kept
    [InlineData(1, 1, 1, 0, 100)] // bogus average below 20 ms
    public async Task CascadeUsesAverageKeptFrameDuration(int repeats, byte first, byte second, byte third, int expectedMs)
    {
        var data = BuildGif(4, repeats);
        var controls = Enumerable.Range(0, data.Length - 7)
            .Where(i => data[i] == 0x21 && data[i + 1] == 0xF9 && data[i + 2] == 0x04).ToArray();
        byte[] delays = { first, second, third }; // GIF delays are hundredths of a second
        for (int i = 0; i < controls.Length; i++) { data[controls[i] + 4] = delays[i % 3]; data[controls[i] + 5] = 0; }
        var path = Path.Combine(Path.GetTempPath(), "ccp-cascade-timing-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, data);
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var (frames, delay) = ChaosGifCascadeOverlay.Decode(path, 400, true);
                try { Assert.True(frames.Count > 1); Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), delay); }
                finally { foreach (var frame in frames) frame.Dispose(); }
                return Task.CompletedTask;
            });
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CascadeDecodeWaitsForTheSharedTwoPermitGate(bool animated)
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-cascade-gate-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(path, Convert.FromBase64String(TinyGif));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                using var entered = new CountdownEvent(2);
                using var release = new ManualResetEventSlim();
                using var started = new ManualResetEventSlim();
                Task Hold() => Task.Factory.StartNew(() => SpiralOverlay.RunGatedDecode(() =>
                {
                    entered.Signal(); release.Wait(); return 0;
                }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                var first = Hold(); var second = Hold();
                Task<(System.Collections.Generic.List<global::Avalonia.Media.Imaging.Bitmap> Frames, TimeSpan Delay)>? decode = null;
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "two decodes should enter the gate");
                    decode = Task.Factory.StartNew(() =>
                    {
                        started.Set();
                        return ChaosGifCascadeOverlay.Decode(path, 400, animated);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
                    Assert.False(decode.Wait(TimeSpan.FromMilliseconds(300)), "a third decode must wait");
                }
                finally
                {
                    release.Set();
                    await Task.WhenAll(first, second);
                    if (decode != null)
                    {
                        var result = await decode;
                        foreach (var frame in result.Frames) frame.Dispose();
                    }
                }
            });
        }
        finally { File.Delete(path); }
    }

    private static byte[] BuildGif(int size, int repeats)
    {
        var data = Convert.FromBase64String(TinyGif);
        data[6] = (byte)size; data[7] = (byte)(size >> 8);
        data[8] = (byte)size; data[9] = (byte)(size >> 8);
        int firstFrame = Enumerable.Range(0, data.Length - 2)
            .First(i => data[i] == 0x21 && data[i + 1] == 0xF9 && data[i + 2] == 0x04);
        using var result = new MemoryStream();
        result.Write(data, 0, firstFrame);
        for (int i = 0; i < repeats; i++) result.Write(data, firstFrame, data.Length - firstFrame - 1);
        result.WriteByte(0x3B);
        return result.ToArray();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoEndQuarantinesCascadeForThreeSeconds(bool forced)
    {
        var video = new MandatoryVideoScheduler(new GateVideoHost());
        try
        {
            await WithDetonationRun(async () =>
            {
                var now = DateTime.UnixEpoch;
                BubbleOverlay.Field.Clock = () => now;
                Assert.True(video.Trigger(path: "gate-test.mp4")); // an existing, non-Chaos video
                if (forced) video.ForceCleanup(); else video.End();
                Assert.False(video.IsPlaying);
                var pick = ChaosGifCascadeOverlay.LastPick;
                Detonate(new GifCascadePayload());
                Assert.Same(pick, ChaosGifCascadeOverlay.LastPick);
                now = now.AddMilliseconds(2999);
                Detonate(new GifCascadePayload());
                Assert.Same(pick, ChaosGifCascadeOverlay.LastPick);
                now = now.AddMilliseconds(1);
                Assert.False(ChaosRunHost.HeavyEffectActive);
                Detonate(new GifCascadePayload());
                Assert.NotSame(pick, ChaosGifCascadeOverlay.LastPick);
                await ChaosGifCascadeOverlay.LastPick!;
                Dispatcher.UIThread.RunJobs();
                Assert.True(ChaosGifCascadeOverlay.IsRaining);
            }, video);
        }
        finally { video.ForceCleanup(); }
    }

    [Fact]
    public async Task VideoEndDoesNotShortenAnExistingHeavyReservation()
    {
        var video = new MandatoryVideoScheduler(new GateVideoHost());
        await WithDetonationRun(() =>
        {
            var now = DateTime.UnixEpoch;
            BubbleOverlay.Field.Clock = () => now;
            Detonate(new VideoPayload()); // 18-second video floor, WPF :2308
            Assert.True(video.Trigger(path: "gate-test.mp4"));
            video.End();
            now = now.AddSeconds(3);
            Assert.True(ChaosRunHost.HeavyEffectActive);
            now = now.AddSeconds(15);
            Assert.False(ChaosRunHost.HeavyEffectActive);
            return Task.CompletedTask;
        }, video);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("panic")]
    [InlineData("close")]
    public async Task TeardownUnsubscribesFromTheOriginalVideoScheduler(string ending)
    {
        var video = new MandatoryVideoScheduler(new GateVideoHost());
        await WithDetonationRun(() =>
        {
            BubbleOverlay.Field.Clock = () => DateTime.UnixEpoch;
            CoreEngine.Video = new MandatoryVideoScheduler(new GateVideoHost());
            if (ending == "stop") ChaosRunHost.RequestStop();
            else if (ending == "panic") ChaosRunHost.ForceShutdown();
            else ChaosRunHost.Overlay!.Close();
            Assert.False(ChaosRunHost.HeavyEffectActive);
            Assert.True(video.Trigger(path: "gate-test.mp4"));
            video.End(); // the old publisher must not re-arm the ended run's gate
            Assert.False(ChaosRunHost.HeavyEffectActive);
            return Task.CompletedTask;
        }, video);
    }

    private static void Detonate(EffectPayload payload) => ChaosRunHost.OnDetonated(new AmbientBubble
    {
        Spec = new EffectBubbleSpec { VariantId = "htlink", IsLive = true, Payload = payload },
    });

    private static async Task WithDetonationRun(Func<Task> body, MandatoryVideoScheduler? video = null)
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
                var previousVideo = CoreEngine.Video;
                try
                {
                    ChaosRunHost.ForceShutdown();
                    ChaosGifCascadeOverlay.CloseActive();
                    CorePaths.EffectiveAssetsProvider = () => dir;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    global::ConditioningControlPanel.Avalonia.App.StartChaosPayloads();
                    if (video != null) CoreEngine.Video = video;
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
                    CoreEngine.Video = previousVideo;
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
