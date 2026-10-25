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
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>A bare detonation in a real run shakes every visible window through
/// <see cref="ScreenShake"/> like WPF ScreenShakeService: the SAME offset on every window each tick,
/// full amplitude until the end, then each window's own transform back.</summary>
public sealed class ChaosShakeTests
{
    [Fact]
    public async Task A_bare_detonation_shakes_every_window_by_one_offset_at_full_strength_then_restores()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = new SteppedTime();
            var oldAccept = ChaosFxWindow.Accept;
            var oldWindows = ScreenShake.Windows;
            ScreenShake.Clock = clock;
            ChaosFxWindow.Accept = _ => true;   // headless has no X11 window to make click-through
            var rootA = new Border();
            var rootB = new Border();
            var a = new Window { Content = rootA };
            var bw = new Window { Content = rootB };
            ScreenShake.Windows = () => new[] { a, bw };
            try
            {
                a.Show(); bw.Show();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, ShakeIntensity = 1.0 });
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Focus = 0; state.Shields = 0; state.CollarSaves = 0;
                var spec = new EffectBubbleSpec
                    { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() };
                var b = BubbleOverlay.SpawnChaos(spec);
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);   // no focus: it goes off in the grip
                Assert.Equal(1, state.Detonated);

                // WPF :2131 Shake(0.4 + s x 0.5, 380), up to 28 px x intensity.
                double amp = ScreenShake.MAX_OFFSET_PX * Math.Clamp(0.4 + spec.Strength / 100.0 * 0.5, 0, 1);
                var ta = Assert.IsType<TranslateTransform>(rootA.RenderTransform);
                var tb = Assert.IsType<TranslateTransform>(rootB.RenderTransform);
                Assert.True(ScreenShake.IsRunning);

                // Late in the shake (370 of 380 ms) the offset still spans full amplitude: no ease-out.
                clock.Advance(TimeSpan.FromMilliseconds(370));
                double peak = 0;
                for (int i = 0; i < 200; i++)
                {
                    ScreenShake.Step();
                    Assert.Equal(ta.X, tb.X);   // one offset for every window
                    Assert.Equal(ta.Y, tb.Y);
                    Assert.True(Math.Abs(ta.X) <= amp && Math.Abs(ta.Y) <= amp);
                    peak = Math.Max(peak, Math.Abs(ta.X));
                }
                Assert.True(peak > amp * 0.6, $"peak {peak:0.0} of {amp:0.0}");

                clock.Advance(TimeSpan.FromMilliseconds(20));
                ScreenShake.Step();   // past the end
                Assert.Null(rootA.RenderTransform);   // each window's own transform is back
                Assert.Null(rootB.RenderTransform);
                Assert.Equal(0, ScreenShake.TargetCount);
                Assert.False(ScreenShake.IsRunning);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ScreenShake.Reset();
                a.Close(); bw.Close();
                ScreenShake.Clock = TimeProvider.System;
                ChaosFxWindow.Accept = oldAccept;
                ScreenShake.Windows = oldWindows;
            }
            return Task.CompletedTask;
        });
    }
}
