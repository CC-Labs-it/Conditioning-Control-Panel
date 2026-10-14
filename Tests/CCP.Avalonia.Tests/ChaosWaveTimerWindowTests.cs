using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Pocket Watch wave countdown (WPF ChaosWaveTimerOverlay) driven from the run's own
/// stepped tick: born at BeginRun only with the charm, readout + red clock + final-rush blink from
/// ChaosRunHost's wave state, closed by panic.</summary>
public sealed class ChaosWaveTimerWindowTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static void Ticks(int n) { for (int i = 0; i < n; i++) ChaosRunHost.Tick(); }

    [Fact]
    public async Task Pocket_watch_counts_the_wave_down_goes_red_blinks_and_panic_closes_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            ChaosWaveTimerWindow.SkipPlatformChecksForTests = true;
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                // Without the charm: no window at all (WPF :450, :1094).
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.State!.ShowWaveTimer = false;
                ChaosRunHost.Overlay!.FinishCountdown();
                Ticks(8);
                Assert.Null(ChaosWaveTimerWindow.Active);
                ChaosRunHost.ForceShutdown();

                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.State!.ShowWaveTimer = true;
                ChaosRunHost.Overlay!.FinishCountdown();
                var w = ChaosWaveTimerWindow.Active!;   // born at BeginRun, pill still hidden
                Assert.True(w.IsVisible);
                Assert.False(w.Pill.IsVisible);

                Ticks(4);   // 1 s into wave 1 of 3 (20 s waves)
                Assert.True(w.Pill.IsVisible);
                Assert.Equal("WAVE 1/3", w.WaveText.Text);
                Assert.Equal("0:19", w.ClockText.Text);
                Assert.Same(Brushes.White, w.ClockText.Foreground);

                Ticks(200);   // 51 s: the last wave's last ten seconds
                Assert.Equal("LAST WAVE", w.WaveText.Text);
                Assert.Equal("0:09", w.ClockText.Text);
                Assert.Equal(Color.FromRgb(0xFF, 0x5A, 0x5A), ((ISolidColorBrush)w.ClockText.Foreground!).Color);
                Assert.True(w.FinalRush);

                var clock = new SteppedClock();
                w.Time = clock;
                w.SetText(ChaosWaveTimerReadout.For(3, 3, 30, 0));   // re-arm the blink on the stepped clock
                w.SetText(ChaosWaveTimerReadout.For(3, 3, 5, 0));
                clock.Now += TimeSpan.FromMilliseconds(420);
                w.BlinkFrame();
                Assert.Equal(0.25, w.ClockText.Opacity, 3);

                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Assert.Null(ChaosWaveTimerWindow.Active);
                Assert.False(w.IsVisible);

                // A run that ends on its own (Ticks to RunOver -> EndRun) also takes the watch down.
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.State!.ShowWaveTimer = true;
                ChaosRunHost.Overlay!.FinishCountdown();
                var w2 = ChaosWaveTimerWindow.Active!;
                Ticks(260);
                Assert.Null(ChaosWaveTimerWindow.Active);
                Assert.False(w2.IsVisible);
            }
            finally { ChaosWaveTimerWindow.SkipPlatformChecksForTests = false; ChaosRunHost.ForceShutdown(); ChaosWaveTimerWindow.CloseActive(); shell.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task No_compositor_refuses_the_watch_for_the_whole_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();   // headless: no X server, so SetClickThrough fails like a compositor-less desktop
            try
            {
                ChaosWaveTimerWindow.EnsureCreated();
                Assert.Null(ChaosWaveTimerWindow.Active);
                ChaosWaveTimerWindow.Update(1, 3, 15, 0);   // the next tick does not re-create it
                Assert.Null(ChaosWaveTimerWindow.Active);
            }
            finally { ChaosWaveTimerWindow.CloseActive(); }
            return Task.CompletedTask;
        });
    }
}
