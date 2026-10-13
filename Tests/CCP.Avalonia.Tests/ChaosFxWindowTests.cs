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
