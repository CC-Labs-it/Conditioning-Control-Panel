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
    [Fact]
    public async Task Refuses_off_platform_draws_hides_when_idle_and_panic_closes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
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
