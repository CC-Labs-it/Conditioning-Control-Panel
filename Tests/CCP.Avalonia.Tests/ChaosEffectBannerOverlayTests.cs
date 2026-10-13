using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The effect-banner strip from a run's start: ChaosRunHost births it (WPF :410), Show adds
/// one label per id, End fades it out over 380 ms on a stepped clock, run teardown closes it.</summary>
public sealed class ChaosEffectBannerOverlayTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_run_births_the_strip_shows_and_fades_labels_and_teardown_closes_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = new Clock();
            var oldClock = ChaosEffectBannerOverlay.Clock;
            ChaosEffectBannerOverlay.Clock = clock;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                var w = ChaosEffectBannerOverlay.Active;
                Assert.NotNull(w);
                Assert.True(w!.IsVisible);
                Assert.False(w.IsHitTestVisible);
                Assert.Empty(w.Row.Children);
                Assert.False(w.IsAnimating);   // P07: an empty strip costs nothing

                ChaosEffectBannerOverlay.Show("vibe", "VibePopping", Colors.Orange);
                ChaosEffectBannerOverlay.Show("freeze", "Freeze", Colors.LightBlue);
                ChaosEffectBannerOverlay.Show("vibe", "VibePopping", Colors.Orange);   // let it ride
                Assert.Equal(2, w.Row.Children.Count);
                Assert.True(w.IsAnimating);
                var vibe = w.Row.Children[0];

                clock.Now = clock.Now.AddMilliseconds(100); w.Step();
                Assert.Equal(0.5, vibe.Opacity, 3);
                clock.Now = clock.Now.AddMilliseconds(325); w.Step();
                Assert.Equal(1.0, vibe.Opacity, 3);

                ChaosEffectBannerOverlay.End("vibe");
                clock.Now = clock.Now.AddMilliseconds(190); w.Step();
                Assert.Equal(0.5, vibe.Opacity, 3);
                Assert.Equal(2, w.Row.Children.Count);
                clock.Now = clock.Now.AddMilliseconds(190); w.Step();
                Assert.Single(w.Row.Children);

                ChaosEffectBannerOverlay.End("freeze");
                clock.Now = clock.Now.AddMilliseconds(400); w.Step();
                Assert.Empty(w.Row.Children);
                Assert.False(w.IsAnimating);
                Assert.Same(w, ChaosEffectBannerOverlay.Active);   // kept alive while empty (WPF)

                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosEffectBannerOverlay.Active);
                Assert.False(w.IsVisible);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosEffectBannerOverlay.CloseActive();
                ChaosEffectBannerOverlay.Clock = oldClock;
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = false;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_refusal_is_remembered_for_the_run_and_cleared_by_teardown()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            try
            {
                ChaosEffectBannerOverlay.CloseActive();
                ChaosEffectBannerOverlay.Refused = true;   // the platform said no earlier this run
                ChaosEffectBannerOverlay.Show("vibe", "VibePopping", Colors.Orange);
                Assert.Null(ChaosEffectBannerOverlay.Active);   // no rebuild, no second Warning

                ChaosEffectBannerOverlay.CloseActive();   // run teardown
                Assert.False(ChaosEffectBannerOverlay.Refused);
                ChaosEffectBannerOverlay.Show("vibe", "VibePopping", Colors.Orange);
                Assert.NotNull(ChaosEffectBannerOverlay.Active);   // the next run asks again
            }
            finally
            {
                ChaosEffectBannerOverlay.CloseActive();
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = false;
            }
            return Task.CompletedTask;
        });
    }
}
