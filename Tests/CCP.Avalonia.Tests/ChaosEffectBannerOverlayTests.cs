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

    // WPF ChaosModeService.cs:2722-2734, 3017-3027, 3176-3177.
    [Theory]
    [InlineData(1, false, 3)]
    [InlineData(2, false, 7)]
    [InlineData(3, true, 7)]
    public async Task Dvd_toy_launches_labels_tracks_retirement_and_cleans_up(int level, bool maxed, double power)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var levels = ChaosMeta.State.LifetimeBoonLevels;
            bool hadLevel = levels.TryGetValue("porn_dvd", out int oldLevel);
            levels["porn_dvd"] = level;
            var oldCan = ChaosDvdOverlay.CanOverlay;
            var oldPrepare = ChaosDvdOverlay.PrepareWindow;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            ChaosDvdOverlay.CanOverlay = () => true;
            ChaosDvdOverlay.PrepareWindow = (_, _) => true;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.BeginRun();
                ChaosRunHost.StopTimerForTests();
                var state = Assert.IsType<ChaosRunState>(ChaosRunHost.State);
                state.ActiveToys.Clear();
                var toy = new ChaosToyState { Id = "porn_dvd", CooldownSec = 30, ChargesLeft = -1 };
                state.ActiveToys.Add(toy);
                state.ToyPower[toy.Id] = power;
                state.DvdSplitBounces = 2;
                if (maxed) state.MaxedBoons.Add(toy.Id);
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById(toy.Id);
                Assert.False(ChaosDvdOverlay.AnyToyActive);
                Assert.Empty(ChaosEffectBannerOverlay.Active!.Strip.Row);
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById(toy.Id);
                Assert.Equal(maxed ? 2 : 1, ChaosDvdOverlay.Flock.Active.Count);
                Assert.All(ChaosDvdOverlay.Flock.Active, l =>
                {
                    Assert.Equal(Math.Max(5, power), l.RemainingSec);
                    Assert.Equal(level == 1 ? 0.8 : level == 2 ? 0.9 : 1.0, l.FontScale);
                    Assert.Equal(ChaosDvdFlock.BaseSpeed * (level == 1 ? 0.7 : level == 2 ? 0.85 : 1.0),
                        Math.Sqrt(l.Vx * l.Vx + l.Vy * l.Vy), 5);
                    Assert.Equal(2, l.SplitBouncesLeft);
                });
                var banner = Assert.Single(ChaosEffectBannerOverlay.Active!.Strip.Row);
                Assert.Equal("dvd", banner.Id);
                Assert.Equal("Porn DVD", banner.Text);
                Assert.Equal("dvd", banner.ArtKey);
                Assert.True(banner.IsLive);
                ChaosRunHost.UseToyById(toy.Id); // cooldown cannot stack logos or labels
                Assert.Equal(maxed ? 2 : 1, ChaosDvdOverlay.Flock.Active.Count);
                ChaosRunHost.Tick();
                Assert.True(toy.IsEffectActive);
                for (int i = 0; i < 100; i++) ChaosDvdOverlay.Advance(0.1);
                ChaosRunHost.Tick();
                Assert.False(toy.IsEffectActive);
                Assert.False(banner.IsLive);
                toy.CooldownRemainingSec = 0;
                ChaosRunHost.UseToyById(toy.Id);
                Assert.True(ChaosDvdOverlay.AnyToyActive);
                ChaosRunHost.ForceShutdown();
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Null(ChaosEffectBannerOverlay.Active);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosDvdOverlay.CloseActive();
                ChaosEffectBannerOverlay.CloseActive();
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = false;
                if (hadLevel) levels["porn_dvd"] = oldLevel;
                else levels.Remove("porn_dvd");
                ChaosDvdOverlay.CanOverlay = oldCan;
                ChaosDvdOverlay.PrepareWindow = oldPrepare;
            }
            return Task.CompletedTask;
        });
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
