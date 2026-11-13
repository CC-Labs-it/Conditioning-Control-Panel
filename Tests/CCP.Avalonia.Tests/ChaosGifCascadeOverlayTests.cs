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
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;
using ConditioningControlPanel.Avalonia.Platform;

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
            ChaosGifCascadeOverlay? rain2 = null;   // the window opened after the run shutdown decodes too
            var oldClock = ChaosGifCascadeOverlay.Clock;
            var oldSkip = ChaosGifCascadeOverlay.SkipPlatformChecksForTests;
            var oldManual = ChaosGifCascadeOverlay.ManualFramesForTests;
            var oldMotion = CoreSettings.Current.MotionLevel;
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

                var w = rain = ChaosGifCascadeOverlay.Active!;
                Assert.NotNull(w);
                Assert.Equal(TimeSpan.Zero, w.FrameInterval);  // every composed frame, as WPF Rendering
                Assert.Equal(w.DesktopScaling, w.Scale);       // the factor SetOverrideRedirect sizes with
                Assert.True(w.IsVisible);
                Assert.True(ChaosGifCascadeOverlay.IsRaining);
                var video = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance.Scheduler;
                try
                {
                    Assert.True(video.Trigger(path: gif));   // WPF VideoService :1916: held behind the rain
                    Assert.False(video.IsPlaying);           // the head wires CascadeIsRaining (review P2)
                }
                finally { video.ForceCleanup(); }
                clock.Advance(7);
                w.Frame();
                Assert.False(w.Cascade.Spawning);
                Assert.True(ChaosGifCascadeOverlay.IsRaining);
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosMeta.State.LessonsComplete.Remove("blindfold");
                ChaosMeta.State.LessonProgress.Remove("blindfold");
                var spec = ChaosBubbleVariants.Build(ChaosBubbleVariants.All.First(), 1);
                ChaosRunHost.OnDefused(new ConditioningControlPanel.Services.AmbientBubble { Spec = spec });
                Assert.Equal(1, ChaosLessons.Progress("blindfold"));
                ChaosRunHost.ForceShutdown();
                Show(gif);
                w = rain2 = ChaosGifCascadeOverlay.Active!;
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
                ChaosRunHost.ForceShutdown();
                ChaosGifCascadeOverlay.CloseActive();
                if (rain != null) await rain.PendingDecodes;
                if (rain2 != null) await rain2.PendingDecodes;   // Windows keeps tiny.gif locked while one decodes
                Dispatcher.UIThread.RunJobs();
                ChaosGifCascadeOverlay.Clock = oldClock;
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = oldSkip;
                ChaosGifCascadeOverlay.ManualFramesForTests = oldManual;
                CoreSettings.Current.MotionLevel = oldMotion;
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

    [Fact]
    public Task AFailedAnimatedDecodeKeepsItsSlotUntilDespawn() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
            // WPF: a local GIF keeps its slot until despawn; ReleaseSlotIfStill is for remote clips only.
            Assert.Equal(1, w.Cascade.AnimatedAlive);
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
            await AFailedAnimatedDecodeKeepsItsSlotUntilDespawn();
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

    [Fact]
    public void ClosingCancelsPendingImagePick()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rain-close-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        try
        {
            AvaloniaTestDispatcher.Run(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                ChaosGifCascadeOverlay.CloseActive();
                ChaosGifCascadeOverlay.SkipPlatformChecksForTests = true;
                ChaosGifCascadeOverlay.ManualFramesForTests = true;
                ChaosGifCascadeOverlay.PickForTests = (_, _) => new() { gif };
                try
                {
                    ChaosGifCascadeOverlay.Show(1, 3, 400, 3.6, 0.9, 0.45);
                    ChaosGifCascadeOverlay.LastPick!.GetAwaiter().GetResult();
                    // The pick has posted the show, but it has not run on the UI thread.
                    ChaosGifCascadeOverlay.CloseActive();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Null(ChaosGifCascadeOverlay.Active);
                }
                finally
                {
                    ChaosGifCascadeOverlay.CloseActive();
                    ChaosGifCascadeOverlay.PickForTests = ChaosGifCascadeOverlay.PickFiles;
                    ChaosGifCascadeOverlay.SkipPlatformChecksForTests = false;
                    ChaosGifCascadeOverlay.ManualFramesForTests = false;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    // Restack b62 (#3762): WPF ChaosModeService.cs:2164-2174,2260,2308-2309. A mandatory video that ends
    // mid-run keeps the heavy gate shut 3 s more, never shortens a floor, and a dropped detonation says so.
    private sealed class GateVideoHost : global::ConditioningControlPanel.Services.IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(global::ConditioningControlPanel.Services.AttentionVerdict verdict, int ms, Action then) => then();
    }

    private static async Task WithCountingRun(global::ConditioningControlPanel.Services.MandatoryVideoScheduler? video,
        Func<Func<DateTime>, Action<DateTime>, Func<int>, Task> body)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var now = DateTime.UnixEpoch;
            var previousClock = BubbleOverlay.Field.Clock;
            var previousFirer = EffectPayload.Firer;
            var previousVideo = CoreEngine.Video;
            int fired = 0;
            try
            {
                ChaosRunHost.ForceShutdown();
                BubbleOverlay.Field.Clock = () => now;
                EffectPayload.Firer = _ => fired++;
                if (video != null) CoreEngine.Video = video;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                await body(() => now, t => now = t, () => fired);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                video?.ForceCleanup();
                BubbleOverlay.Field.Clock = previousClock;
                EffectPayload.Firer = previousFirer;
                CoreEngine.Video = previousVideo;
            }
        });
    }

    private static void DetonateSpec(EffectPayload payload) =>
        ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = payload });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Video_end_quarantines_heavy_detonations_for_three_seconds(bool forced)
    {
        var video = new global::ConditioningControlPanel.Services.MandatoryVideoScheduler(new GateVideoHost());
        await WithCountingRun(video, (getNow, setNow, fired) =>
        {
            Assert.True(video.Trigger(path: "gate-test.mp4"));
            if (forced) video.ForceCleanup(); else video.End();
            Assert.False(video.IsPlaying);
            DetonateSpec(new GifCascadePayload());
            Assert.Equal(0, fired());
            Assert.Contains(ConditioningControlPanel.Localization.Loc.GetF("chaos_heavy_fizzle", new GifCascadePayload().DisplayName),
                ChaosRunHost.State!.RecentEvents);
            setNow(getNow().AddMilliseconds(2999));
            DetonateSpec(new GifCascadePayload());
            Assert.Equal(0, fired());
            setNow(getNow().AddMilliseconds(1));
            DetonateSpec(new GifCascadePayload());
            Assert.Equal(1, fired());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Video_end_never_shortens_an_existing_heavy_reservation()
    {
        var video = new global::ConditioningControlPanel.Services.MandatoryVideoScheduler(new GateVideoHost());
        await WithCountingRun(video, (getNow, setNow, fired) =>
        {
            var start = getNow();
            DetonateSpec(new GifCascadePayload());   // reserves DURATION_SEC + 5 = 11 s
            Assert.Equal(1, fired());
            Assert.True(video.Trigger(path: "gate-test.mp4"));
            video.End();
            setNow(start.AddSeconds(10.999));
            DetonateSpec(new GifCascadePayload());
            Assert.Equal(1, fired());
            setNow(start.AddSeconds(11));
            DetonateSpec(new GifCascadePayload());
            Assert.Equal(2, fired());
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("panic")]
    public async Task Run_teardown_unsubscribes_from_the_original_video_scheduler(string ending)
    {
        var video = new global::ConditioningControlPanel.Services.MandatoryVideoScheduler(new GateVideoHost());
        await WithCountingRun(video, (getNow, setNow, fired) =>
        {
            CoreEngine.Video = new global::ConditioningControlPanel.Services.MandatoryVideoScheduler(new GateVideoHost());
            if (ending == "stop") ChaosRunHost.RequestStop();
            else ChaosRunHost.ForceShutdown();
            Assert.True(video.Trigger(path: "gate-test.mp4"));
            video.End();   // the old publisher must not re-arm the ended run's gate
            DetonateSpec(new GifCascadePayload());
            Assert.Equal(1, fired());
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
