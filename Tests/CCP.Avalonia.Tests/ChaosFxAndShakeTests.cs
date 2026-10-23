using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The run's juice from a real run: a bare detonation pulses the run's ChaosFxWindow
/// (chaos-fx-window lane) with WPF's malus colour and strength, shakes every visible window through
/// the shared Deeper ScreenShake (WPF ScreenShakeService), and panic closes the window.</summary>
public sealed class ChaosFxAndShakeTests
{
    [Fact]
    public async Task A_bare_detonation_pulses_the_fx_window_and_shakes_the_windows_then_panic_closes_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = new SteppedTime();
            var oldAccept = ChaosFxWindow.Accept;
            var oldWindows = ScreenShake.WindowsProvider;
            ChaosFxWindow.Clock = ScreenShake.Clock = clock;
            ChaosFxWindow.Accept = _ => true;   // headless has no X11 window to make click-through
            var root = new Border();
            var host = new Window { Content = root };
            ScreenShake.WindowsProvider = () => new[] { host };
            try
            {
                host.Show();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, ShakeIntensity = 1.0 });
                ChaosRunHost.Overlay!.FinishCountdown();
                var fx = ChaosRunHost.Fx!;
                Assert.False(fx.IsAnimating);   // P07: idle until a pulse

                var state = ChaosRunHost.State!;
                state.Focus = 0; state.Shields = 0; state.CollarSaves = 0;
                var spec = new EffectBubbleSpec
                    { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() };
                var b = BubbleOverlay.SpawnChaos(spec);
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);   // no focus: it goes off in the grip
                Assert.Equal(1, state.Detonated);
                double s = spec.Strength / 100.0;

                // Shake: 0.4 + s x 0.5 for 380 ms, up to 28 px x intensity.
                double amp = ScreenShake.MaxOffsetPx * Math.Clamp(0.4 + s * 0.5, 0, 1);
                var t = Assert.IsType<TranslateTransform>(root.RenderTransform);
                Assert.True(ScreenShake.IsRunning);
                ScreenShake.Tick();
                Assert.True(t.X != 0 && Math.Abs(t.X) <= amp && Math.Abs(t.Y) <= amp);

                // The last pulse is the bare hit's red malus, 0.4 + s x 0.35 (WPF :2131), on the pulse layer.
                Assert.True(fx.IsAnimating);
                var rim = Assert.IsAssignableFrom<IRadialGradientBrush>(fx.VignetteLayer.Background);
                Assert.Equal(Color.FromArgb(255, 255, 50, 50), rim.GradientStops[2].Color);
                clock.Advance(TimeSpan.FromMilliseconds(40));
                fx.Frame();
                Assert.Equal(ChaosFxEnvelope.Pulse(0, 0.4 + s * 0.35).Sample(40), fx.VignetteLayer.Opacity, 3);
                clock.Advance(TimeSpan.FromMilliseconds(300));
                fx.Frame();
                Assert.Equal(0, fx.VignetteLayer.Opacity);

                ScreenShake.Tick();   // 340 ms on: still inside the 380 ms shake
                Assert.Same(t, root.RenderTransform);
                clock.Advance(TimeSpan.FromMilliseconds(50));
                ScreenShake.Tick();   // past it
                Assert.Null(root.RenderTransform);   // the window's own transform is back
                Assert.Equal(0, ScreenShake.TargetCount);
                Assert.False(ScreenShake.IsRunning);

                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosRunHost.Fx);
                Assert.False(fx.IsVisible);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ScreenShake.Stop();
                host.Close();
                ChaosFxWindow.Clock = ScreenShake.Clock = TimeProvider.System;
                ChaosFxWindow.Accept = oldAccept;
                ScreenShake.WindowsProvider = oldWindows;
            }
            return Task.CompletedTask;
        });
    }
}
