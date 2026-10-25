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

/// <summary>WPF ChaosFxWindow on a stepped clock: the run opens it at BeginRun, the 4 Hz tick drives
/// the heat tint, impact pulses snap and fade on WPF's timings, and the run's end and panic close it.</summary>
public sealed class ChaosFxWindowTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Task Run(Action<SteppedTime> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var clock = new SteppedTime();
        var accept = ChaosFxWindow.Accept;
        ChaosFxWindow.Clock = clock;
        ChaosFxWindow.Accept = _ => true;   // headless has no X11 window to make click-through
        try { body(clock); }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosFxWindow.Clock = TimeProvider.System;
            ChaosFxWindow.Accept = accept;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task A_run_opens_the_fx_window_drives_the_heat_tint_and_closes_it_on_panic()
    {
        await Run(clock =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            Assert.Null(ChaosRunHost.Fx);                 // not during the countdown
            ChaosRunHost.Overlay!.FinishCountdown();      // BeginRun
            var fx = ChaosRunHost.Fx!;
            Assert.True(fx.IsVisible);
            Assert.False(fx.IsHitTestVisible);

            ChaosRunHost.State!.Heat = 1.0;
            ChaosRunHost.Tick();                          // UpdateHeatTint: full heat -> 0.30
            clock.Advance(TimeSpan.FromMilliseconds(200));
            fx.Frame();
            Assert.Equal(0.15, fx.HeatLayer.Opacity, 3);
            clock.Advance(TimeSpan.FromMilliseconds(250));
            fx.Frame();
            Assert.Equal(0.30, fx.HeatLayer.Opacity, 3);
            Assert.False(fx.IsAnimating);                 // held: no frames once it's there
            var stops = ((IRadialGradientBrush)fx.HeatLayer.Background!).GradientStops;
            Assert.Equal(Color.FromArgb(255, 255, 90, 40), stops[2].Color);
            Assert.Equal(0.40, stops[1].Offset, 3);

            ChaosRunHost.ForceShutdown();                 // panic
            Assert.Null(ChaosRunHost.Fx);
            Assert.False(fx.IsVisible);
        });
    }

    [Fact]
    public async Task The_run_ending_normally_closes_the_fx_window_before_the_recap()
    {
        await Run(_ =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            var fx = ChaosRunHost.Fx!;
            ChaosRunHost.RequestStop();                   // EndRun, not panic: the overlay stays for the recap
            Assert.NotNull(ChaosRunHost.Overlay);
            Assert.Null(ChaosRunHost.Fx);
            Assert.False(fx.IsVisible);
        });
    }

    [Fact]
    public async Task Colour_flashes_off_means_no_heat_tint()
    {
        await Run(clock =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, ColorFlashesEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.State!.Heat = 1.0;
            ChaosRunHost.Tick();
            Assert.False(ChaosRunHost.Fx!.IsAnimating);
            Assert.Null(ChaosRunHost.Fx.HeatLayer.Background);
        });
    }

    [Theory]
    [InlineData("bare", 255, 50, 50, 0.575)]
    [InlineData("shield", 80, 160, 255, 0.28)]
    [InlineData("chain", 122, 224, 255, 0.22)]
    [InlineData("collar", 255, 215, 0, 0.32)]
    [InlineData("unleashed", 255, 215, 0, 0.50)]
    public async Task Detonation_pulses_match_the_protection_that_paid(string protection, int r, int g, int b, double strength)
    {
        await Run(clock =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            var state = ChaosRunHost.State!;
            state.Shields = protection == "shield" ? 1 : 0;
            state.CollarSaves = protection is "collar" or "unleashed" ? 1 : 0;
            state.UnleashedEnabled = protection == "unleashed";
            var bubble = ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload { Strength = 50 } });
            if (protection == "chain")
            {
                state.DefuseInvulnMs = 1000;
                ChaosRunHost.OnDefused(bubble);
            }
            ChaosRunHost.OnDetonated(bubble);
            AssertPulse(clock, Color.FromRgb((byte)r, (byte)g, (byte)b), strength);
        });
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 10)]
    [InlineData(false, true, 25)]
    public async Task Pops_and_defuses_end_with_the_WPF_combo_pulse(bool prism, bool defuse, int combo)
    {
        await Run(clock =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.State!.Combo = combo - 1;
            var bubble = ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                { VariantId = "pink", IsPrism = prism, IsLive = defuse, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });
            if (defuse) ChaosRunHost.OnDefused(bubble);
            else ChaosRunHost.OnBubblePopped(bubble);
            AssertPulse(clock, combo % 10 == 0 ? Color.FromRgb(255, 200, 60) : Color.FromRgb(255, 170, 80),
                combo % 10 == 0 ? 0.4 : 0.04 + 0.08 * ((combo % 10) / 10.0));
        });
    }

    [Fact]
    public async Task Colour_flashes_off_and_manual_pause_suppress_bubble_pulses()
    {
        await Run(clock =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, ColorFlashesEnabled = false });
            ChaosRunHost.Overlay!.FinishCountdown();
            var bubble = ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                { VariantId = "pink", FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });
            ChaosRunHost.OnBubblePopped(bubble);
            ChaosRunHost.OnDefused(bubble);
            ChaosRunHost.OnDetonated(bubble);
            Assert.Null(ChaosRunHost.Fx!.VignetteLayer.Background);
            ChaosRunHost.State!.Config.ColorFlashesEnabled = true;
            ChaosRunHost.ToggleManualPause();
            ChaosRunHost.OnBubblePopped(bubble);
            ChaosRunHost.OnDefused(bubble);
            ChaosRunHost.OnDetonated(bubble);
            Assert.Null(ChaosRunHost.Fx.VignetteLayer.Background);
        });
    }

    [Fact]
    public async Task Big_combo_crossings_fire_once_per_streak_and_reset_after_a_bare_hit()
    {
        await Run(clock =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            var state = ChaosRunHost.State!;
            var bubble = ConditioningControlPanel.Avalonia.Views.Overlays.BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                { VariantId = "pink", FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });
            state.Combo = 24;
            ChaosRunHost.OnBubblePopped(bubble);
            Assert.Contains("🔥🔥 STREAK x25!", state.RecentEvents);
            state.RecentEvents.Clear();
            state.Combo = 24;
            ChaosRunHost.OnBubblePopped(bubble);
            Assert.DoesNotContain("🔥🔥 STREAK x25!", state.RecentEvents);
            state.Shields = state.CollarSaves = 0;
            ChaosRunHost.OnDetonated(bubble);
            state.Combo = 24;
            ChaosRunHost.OnBubblePopped(bubble);
            Assert.Contains("🔥🔥 STREAK x25!", state.RecentEvents);
        });
    }

    private static void AssertPulse(SteppedTime clock, Color color, double strength)
    {
        var fx = ChaosRunHost.Fx!;
        var stops = ((IRadialGradientBrush)fx.VignetteLayer.Background!).GradientStops;
        Assert.Equal(color, stops[2].Color);
        clock.Advance(TimeSpan.FromMilliseconds(40));
        fx.Frame();
        Assert.Equal(Math.Clamp(0.22 + strength * 0.5, 0.15, 0.72), fx.VignetteLayer.Opacity, 6);
        clock.Advance(TimeSpan.FromMilliseconds(260));
        fx.Frame();
        Assert.Equal(0, fx.VignetteLayer.Opacity);
    }

    [Fact]
    public async Task A_pulse_snaps_up_in_40ms_and_fades_out_by_300ms_over_a_held_edge()
    {
        await Run(clock =>
        {
            var fx = new ChaosFxWindow();
            fx.Show();
            try
            {
                fx.BeginEdgeHold(Color.FromRgb(150, 210, 255), 0.5);
                fx.Pulse(Colors.Red, 0.5);
                clock.Advance(TimeSpan.FromMilliseconds(40));
                fx.Frame();
                Assert.Equal(0.47, fx.VignetteLayer.Opacity, 3);
                clock.Advance(TimeSpan.FromMilliseconds(300));
                fx.Frame();
                Assert.Equal(0, fx.VignetteLayer.Opacity, 3);
                Assert.Equal(0.475, fx.EdgeLayer.Opacity, 3);   // the hold rides its own layer
                Assert.False(fx.IsAnimating);
            }
            finally { fx.Close(); }
        });
    }
}
