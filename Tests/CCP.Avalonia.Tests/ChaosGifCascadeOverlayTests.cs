using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            ChaosGifCascadeOverlay? rain = null;
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

                var w = rain = ChaosGifCascadeOverlay.Active!;
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
                if (rain != null) await rain.PendingDecodes;
                Dispatcher.UIThread.RunJobs();
                ChaosGifCascadeOverlay.Clock = TimeProvider.System;
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                ChaosGifCascadeOverlay.ManualFramesForTests = false;
            }
        });
        Directory.Delete(dir, true);
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
                ChaosGifCascadeOverlay? rain = null;
                var previousAssets = CorePaths.EffectiveAssetsProvider;
                var previousFirer = EffectPayload.Firer;
                var previousDuration = EffectPayload.GlobalDurationMult;
                var previousAudio = CoreAudio.PlayOneShotProvider;
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
                    var cues = new List<string>();
                    CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(Path.GetFileNameWithoutExtension(path));
                    ChaosRunHost.State!.Focus = 0;
                    BubbleOverlay.Press(bubble, bubble.CenterX, bubble.CenterY);
                    Assert.Contains("fx_rain_start", cues); // detonation must preserve the htlink variant
                    Assert.NotNull(ChaosGifCascadeOverlay.LastPick);
                    await ChaosGifCascadeOverlay.LastPick!;
                    Dispatcher.UIThread.RunJobs();
                    var w = rain = ChaosGifCascadeOverlay.Active!;
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
                    try
                    {
                        ChaosRunHost.ForceShutdown();
                        ChaosGifCascadeOverlay.CloseActive();
                        if (rain != null) await rain.PendingDecodes;
                        Dispatcher.UIThread.RunJobs();
                        ChaosGifCascadeOverlay.Clock = TimeProvider.System;
                        ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                        ChaosGifCascadeOverlay.ManualFramesForTests = false;
                    }
                    finally
                    {
                        CorePaths.EffectiveAssetsProvider = previousAssets;
                        EffectPayload.Firer = previousFirer;
                        EffectPayload.GlobalDurationMult = previousDuration;
                        CoreAudio.PlayOneShotProvider = previousAudio;
                    }
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
            var previousClock = BubbleOverlay.Field.Clock;
            BubbleOverlay.Field.Clock = () => now;
            int fired = 0;
            var previousFirer = EffectPayload.Firer;
            EffectPayload.Firer = _ => fired++;
            void Detonate(EffectPayload payload) =>
                ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = payload });
            try
            {
                ChaosRunHost.ForceShutdown();
                Detonate(new GifCascadePayload());
                Detonate(new GifCascadePayload());
                Detonate(new VideoPayload());
                Assert.Equal(1, fired);
                Detonate(new FlashPayload());
                Assert.Equal(2, fired); // light effects still fire
                now = now.AddSeconds(10.9);
                Detonate(new GifCascadePayload());
                Assert.Equal(2, fired);
                now = now.AddSeconds(0.2);
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                ChaosGifCascadeOverlay.ManualFramesForTests = true;
                Show("missing.gif"); // decoding may fail, but the faller is still real rain
                Detonate(new GifCascadePayload());
                Assert.Equal(2, fired);
                ChaosGifCascadeOverlay.CloseActive();
                Detonate(new GifCascadePayload());
                Assert.Equal(3, fired);
                ChaosRunHost.ForceShutdown();
                Detonate(new GifCascadePayload());
                Assert.Equal(4, fired);
            }
            finally
            {
                BubbleOverlay.Field.Clock = previousClock;
                EffectPayload.Firer = previousFirer;
                ChaosRunHost.ForceShutdown();
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                ChaosGifCascadeOverlay.ManualFramesForTests = false;
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:2321-2337: sound is chosen by variant and never plays for a dropped rain.
    [Theory]
    [InlineData("htlink", true)]
    [InlineData("other", false)]
    public async Task Rain_stinger_uses_variant_and_only_plays_after_heavy_gate_accepts(string variant, bool sounds)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var now = DateTime.UtcNow;
            var previousClock = BubbleOverlay.Field.Clock;
            BubbleOverlay.Field.Clock = () => now;
            var previousFirer = EffectPayload.Firer;
            var previousAudio = CoreAudio.PlayOneShotProvider;
            var previousVolume = CoreSettings.Current.MasterVolume;
            CoreSettings.Current.MasterVolume = 100;
            var events = new List<(string Key, float Volume)>();
            CoreAudio.PlayOneShotProvider = (path, volume, _, _, _) =>
                events.Add((Path.GetFileNameWithoutExtension(path), volume));
            EffectPayload.Firer = _ => events.Add(("fire", 0f));
            var spec = new EffectBubbleSpec { VariantId = variant, Payload = new GifCascadePayload() };
            var expected = sounds
                ? new[] { ("fx_rain_start", 0.45f), ("fire", 0f) }
                : new[] { ("fire", 0f) };
            try
            {
                ChaosRunHost.ForceShutdown();
                ChaosRunHost.FirePayloadForDetonation(spec);
                Assert.Equal(expected, events); // accepted: variant sound at 0.45, then payload
                ChaosRunHost.FirePayloadForDetonation(spec);
                Assert.Equal(expected, events); // dropped: neither sound nor payload
                now = now.AddSeconds(11.1);
                ChaosRunHost.FirePayloadForDetonation(spec);
                Assert.Equal(expected.Concat(expected), events);
            }
            finally
            {
                BubbleOverlay.Field.Clock = previousClock;
                EffectPayload.Firer = previousFirer;
                CoreAudio.PlayOneShotProvider = previousAudio;
                CoreSettings.Current.MasterVolume = previousVolume;
                ChaosRunHost.ForceShutdown();
            }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("heavy")]
    [InlineData("htlink")]
    [InlineData("other")]
    public async Task Frozen_clock_cases_restore_the_callers_clock(string testCase)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var previousClock = BubbleOverlay.Field.Clock;
            Func<DateTime> sentinel = () => DateTime.UnixEpoch;
            BubbleOverlay.Field.Clock = sentinel;
            try
            {
                if (testCase == "heavy")
                    await Heavy_detonations_are_dropped_during_pick_grace_and_real_rain_then_reset_on_shutdown();
                else
                    await Rain_stinger_uses_variant_and_only_plays_after_heavy_gate_accepts(testCase, testCase == "htlink");
                Assert.Same(sentinel, BubbleOverlay.Field.Clock);
            }
            finally { BubbleOverlay.Field.Clock = previousClock; }
        });
    }

    [Fact]
    public async Task Teardown_waits_for_older_decode_even_after_newest_decode_finishes()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rain-readers-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                using var release = new ManualResetEventSlim();
                var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int readers = 0;
                ChaosGifCascadeOverlay? rain = null;
                Task? first = null;
                try
                {
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                    ChaosGifCascadeOverlay.ManualFramesForTests = true;
                    ChaosGifCascadeOverlay.BeforeDecodeForTests = path =>
                    {
                        if (Interlocked.Increment(ref readers) != 1) return;
                        using var stream = File.OpenRead(path);
                        reading.SetResult();
                        release.Wait(); // hold a real reader, as the Windows CI failure did
                    };
                    Show(gif);
                    rain = ChaosGifCascadeOverlay.Active!;
                    first = ChaosGifCascadeOverlay.LastDecode!;
                    await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Show(gif); // restart removes the first faller while its decode still reads
                    await Decoded();
                    Assert.False(first.IsCompleted);
                    var pending = rain.PendingDecodes;
                    Assert.False(pending.IsCompleted); // newest alone is insufficient
                    ChaosGifCascadeOverlay.CloseActive();
                    Assert.Null(ChaosGifCascadeOverlay.Active); // panic still closes immediately
                    Assert.False(pending.IsCompleted);
                    release.Set();
                    await pending;
                    Dispatcher.UIThread.RunJobs(); // dispose late frames for removed fallers
                    using var exclusive = File.Open(gif, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                finally
                {
                    release.Set();
                    ChaosGifCascadeOverlay.CloseActive();
                    if (first != null) await first;
                    if (rain != null) await rain.PendingDecodes;
                    Dispatcher.UIThread.RunJobs();
                    ChaosGifCascadeOverlay.BeforeDecodeForTests = null;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                    ChaosGifCascadeOverlay.ManualFramesForTests = false;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("live")]
    [InlineData("heavy")]
    [InlineData("htlink")]
    [InlineData("other")]
    public async Task Nested_cases_restore_the_callers_providers_duration_and_volume(string testCase)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var previous = (CorePaths.EffectiveAssetsProvider, EffectPayload.Firer,
                EffectPayload.GlobalDurationMult, CoreAudio.PlayOneShotProvider, CoreSettings.Current.MasterVolume);
            Func<string> assets = () => Path.Combine(TestUserDataProfile.Root, "sentinel-assets");
            Action<EffectPayload> firer = _ => { };
            CorePaths.EffectiveAssetsProvider = assets;
            EffectPayload.Firer = firer;
            EffectPayload.GlobalDurationMult = 1.25;
            CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
            var audio = CoreAudio.PlayOneShotProvider;
            CoreSettings.Current.MasterVolume = 37;
            try
            {
                if (testCase == "live")
                    await Live_bubble_detonation_fires_payload_with_global_duration_and_panic_cancels_pending_pick();
                else if (testCase == "heavy")
                    await Heavy_detonations_are_dropped_during_pick_grace_and_real_rain_then_reset_on_shutdown();
                else
                    await Rain_stinger_uses_variant_and_only_plays_after_heavy_gate_accepts(testCase, testCase == "htlink");
                Assert.Same(assets, CorePaths.EffectiveAssetsProvider);
                Assert.Same(firer, EffectPayload.Firer);
                Assert.Equal(1.25, EffectPayload.GlobalDurationMult);
                Assert.Same(audio, CoreAudio.PlayOneShotProvider);
                Assert.Equal(37, CoreSettings.Current.MasterVolume);
            }
            finally
            {
                (CorePaths.EffectiveAssetsProvider, EffectPayload.Firer, EffectPayload.GlobalDurationMult,
                    CoreAudio.PlayOneShotProvider, CoreSettings.Current.MasterVolume) = previous;
            }
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
