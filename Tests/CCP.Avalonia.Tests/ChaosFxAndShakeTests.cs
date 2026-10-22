using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The run's juice from a real run: BeginRun births the click-through colour vignette
/// (WPF ChaosModeService :500), a bare detonation pulses it with WPF ChaosFxWindow.Pulse's envelope
/// and shakes every visible window like WPF ScreenShakeService, and run teardown closes it.</summary>
public sealed class ChaosFxAndShakeTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task A_bare_detonation_pulses_the_vignette_and_shakes_the_windows_then_teardown_closes_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = new Clock();
            var oldFx = ChaosFxWindow.Clock;
            var oldShake = ScreenShake.Clock;
            var oldWindows = ScreenShake.Windows;
            var oldRng = ScreenShake.Rng;
            ChaosFxWindow.Clock = ScreenShake.Clock = clock;
            ChaosFxWindow.SkipPlatformChecksForTests = true;
            ScreenShake.Rng = new Random(7);
            var root = new Border();
            var host = new Window { Content = root };
            ScreenShake.Windows = () => new[] { host };
            try
            {
                host.Show();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, ShakeIntensity = 1.0 });
                Assert.Null(ChaosRunHost.Fx);   // born at BeginRun, not during the countdown
                ChaosRunHost.Overlay!.FinishCountdown();
                var fx = ChaosRunHost.Fx;
                Assert.NotNull(fx);
                Assert.True(fx!.IsVisible);
                Assert.False(fx.IsHitTestVisible);
                Assert.False(fx.IsAnimating);   // P07: idle until a pulse

                var state = ChaosRunHost.State!;
                state.Focus = 0; state.Shields = 0; state.CollarSaves = 0;
                var spec = new EffectBubbleSpec
                    { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() };
                var b = BubbleOverlay.SpawnChaos(spec);
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);   // no focus: it goes off in the grip
                Assert.Equal(1, state.Detonated);
                double s = spec.Strength / 100.0;

                // Shake: 0.4 + s x 0.5 for 380 ms, the same jitter on every window, up to 28 px x intensity.
                double amp = ScreenShake.MAX_OFFSET_PX * Math.Clamp(0.4 + s * 0.5, 0, 1);
                var t = Assert.IsType<TranslateTransform>(root.RenderTransform);
                Assert.True(ScreenShake.IsRunning);
                ScreenShake.Step();
                Assert.True(t.X != 0 && Math.Abs(t.X) <= amp && Math.Abs(t.Y) <= amp);

                // The last pulse is the bare hit's red malus: 0.4 + s x 0.35 (WPF :2131).
                double peak = ChaosFxWindow.PeakFor(0.4 + s * 0.35);
                int rise = GentleVisuals.RiseMs(40);
                Assert.True(fx.IsAnimating);
                var rim = Assert.IsType<RadialGradientBrush>(fx.Vignette.Background);
                Assert.Equal(Color.FromArgb(255, 255, 50, 50), rim.GradientStops[2].Color);
                Assert.Equal(Color.FromArgb(0, 255, 50, 50), rim.GradientStops[1].Color);
                clock.Now = clock.Now.AddMilliseconds(rise);
                fx.Step();
                Assert.Equal(peak, fx.Vignette.Opacity, 3);
                clock.Now = clock.Now.AddMilliseconds(ChaosFxWindow.FADE_MS / 2.0);
                fx.Step();
                Assert.Equal(peak / 2, fx.Vignette.Opacity, 3);
                clock.Now = clock.Now.AddMilliseconds(ChaosFxWindow.FADE_MS);
                fx.Step();
                Assert.Equal(0, fx.Vignette.Opacity);
                Assert.False(fx.IsAnimating);

                ScreenShake.Step();   // 430 ms on: past the 380 ms shake
                Assert.Null(root.RenderTransform);   // the window's own transform is back
                Assert.Equal(0, ScreenShake.TargetCount);   // holds no window past its shake
                Assert.False(ScreenShake.IsRunning);

                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosRunHost.Fx);
                Assert.False(fx.IsVisible);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ScreenShake.Reset();
                host.Close();
                ChaosFxWindow.Clock = oldFx;
                ChaosFxWindow.SkipPlatformChecksForTests = false;
                ScreenShake.Clock = oldShake;
                ScreenShake.Windows = oldWindows;
                ScreenShake.Rng = oldRng;
            }
            return Task.CompletedTask;
        });
    }
}
