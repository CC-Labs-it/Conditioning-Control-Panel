using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ChaosFieldFxOverlay's lifecycle on this head: refuse without a composited
/// click-through platform, show on first draw, hide and stop the frame clock when idle, close on panic.</summary>
public sealed class ChaosFieldFxOverlayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_precreates_hidden_surface_with_pin_preference_and_end_closes_it(bool pinned)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = global::ConditioningControlPanel.CoreSettings.Current;
            bool pin = settings.ChaosPinOnTop;
            var savedRaise = ChaosFieldFxOverlay.RaiseWindow;
            int raises = 0;
            var host = typeof(ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost);
            try
            {
                settings.ChaosPinOnTop = pinned;
                ChaosFieldFxOverlay.RaiseWindow = _ => raises++;
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.StartRun(
                    new ConditioningControlPanel.Services.Chaos.ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                var w = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                Assert.False(w.IsVisible);
                Assert.False(w.Clock.IsEnabled);
                Assert.Equal(0, raises);
                Assert.Equal(pinned, w.Topmost);
                ChaosFieldFxOverlay.EnsureCreated();
                Assert.Same(w, ChaosFieldFxOverlay.Active);
                ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.Overlay!.FinishCountdown();
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                Assert.Equal(pinned ? 1 : 0, raises);
                w.Step(double.MaxValue);
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                Assert.Equal(pinned ? 2 : 0, raises);
                host.GetMethod("EndRun", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, null);
                Assert.Null(ChaosFieldFxOverlay.Active);
                Assert.False(w.IsVisible);
                Assert.False(w.Clock.IsEnabled);
            }
            finally
            {
                ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.ForceShutdown();
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                settings.ChaosPinOnTop = pin;
                ChaosFieldFxOverlay.RaiseWindow = savedRaise;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Residue_reads_live_gentle_settings_and_close_resets_platform_refusal()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var savedNow = ChaosFieldFxOverlay.NowMs;
            try
            {
                var service = new ConditioningControlPanel.Services.SettingsService();
                global::ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
                global::ConditioningControlPanel.CoreSettings.Current.GentleVisuals = true;
                global::ConditioningControlPanel.CoreSettings.Current.GentleFadeSeconds = 1.5;
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.NowMs = () => 0;
                ChaosFieldFxOverlay.Residue(new Point(5, 5), 20, 500);
                var w = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                var frame = new System.Collections.Generic.List<ConditioningControlPanel.Services.Chaos.FieldFxPrim>();
                w.Model.Frame(0, frame);
                Assert.Equal(0, Assert.Single(frame).Opacity);
                w.Model.Frame(1050, frame);
                Assert.Equal(0.4, Assert.Single(frame).Opacity, 6);
                var refusal = typeof(ChaosFieldFxOverlay).GetField("_refused",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                refusal.SetValue(null, true);
                ChaosFieldFxOverlay.CloseActive();
                Assert.False((bool)refusal.GetValue(null)!);
            }
            finally
            {
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                ChaosFieldFxOverlay.NowMs = savedNow;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Refuses_off_platform_draws_hides_when_idle_and_panic_closes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var savedNow = ChaosFieldFxOverlay.NowMs;
            var savedReady = ChaosFieldFxOverlay.PlatformReady;
            double now = 0;
            try
            {
                // Headless is not an X11 window: no stage-sized topmost window that could eat clicks.
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 50, 500);
                Assert.Null(ChaosFieldFxOverlay.Active);
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.PlatformReady = () => false;   // !X11Overlay.IsCompositing: refuse before any window
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 50, 500);
                Assert.Null(ChaosFieldFxOverlay.Active);
                ChaosFieldFxOverlay.PlatformReady = savedReady;

                ChaosFieldFxOverlay.NowMs = () => now;
                ChaosFieldFxOverlay.Ripple(new Point(100, 100), 50, 500);
                var w = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                Assert.True(w.IsVisible);
                Assert.True(w.Clock.IsEnabled);

                now = 250;
                w.Step(now);
                Assert.True(w.IsVisible);
                now = 500;
                w.Step(now);   // ring finished: hidden, clock parked (WPF RemoveTransient -> Hide)
                Assert.False(w.IsVisible);
                Assert.False(w.Clock.IsEnabled);

                ChaosFieldFxOverlay.TrailDot(new Point(5, 5), 1);   // re-shown on the next draw
                Assert.Same(w, ChaosFieldFxOverlay.Active);
                Assert.True(w.IsVisible && w.Clock.IsEnabled);

                PanicSurfaces.All.Single(s => s.Id == "chaos-field-fx").Stop(null);
                Assert.Null(ChaosFieldFxOverlay.Active);
                Assert.False(w.IsVisible);
                Assert.False(w.Clock.IsEnabled);

                // Run teardown closes it too (WPF ChaosModeService CloseActive).
                ChaosFieldFxOverlay.Residue(new Point(5, 5), 20, 500);
                var w2 = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                typeof(ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost)
                    .GetMethod("CleanupAfterRun", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, null);
                Assert.Null(ChaosFieldFxOverlay.Active);
                Assert.False(w2.IsVisible);

                ChaosFieldFxOverlay.SetTether(7, new Point(0, 0), new Point(100, 100));
                var tether = ChaosFieldFxOverlay.Active!;
                // Keep the dispatcher occupied until the worker returns: removal must be queued.
                Task.Run(() => ChaosFieldFxOverlay.ClearTether(7)).GetAwaiter().GetResult();
                Assert.Equal(1, tether.Model.TetherCount);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, tether.Model.TetherCount);
                Assert.False(tether.IsVisible);
                Task.Run(ChaosFieldFxOverlay.CloseActive).GetAwaiter().GetResult();
                Assert.Same(tether, ChaosFieldFxOverlay.Active);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(ChaosFieldFxOverlay.Active);
            }
            finally
            {
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                ChaosFieldFxOverlay.NowMs = savedNow;
                ChaosFieldFxOverlay.PlatformReady = savedReady;
            }
            return Task.CompletedTask;
        });
    }
}
