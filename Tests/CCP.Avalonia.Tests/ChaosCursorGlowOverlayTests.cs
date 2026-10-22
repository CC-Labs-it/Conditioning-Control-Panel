using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Rabbit Caller's cursor halo (WPF Chaos/ChaosCursorGlowOverlay.cs): Arm shows it and follows
/// the pointer with the 0.85..1.12 breath, Disarm stops tracking, panic and run teardown close it, and a
/// platform that cannot make it click-through never shows it.</summary>
public sealed class ChaosCursorGlowOverlayTests
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
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static Task WithSeams(Func<bool> clickThrough, Action<SteppedClock, Action<int, int>> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var (read, make, clock) = (ChaosCursorGlowOverlay.ReadPointer, ChaosCursorGlowOverlay.MakeClickThrough, ChaosCursorGlowOverlay.Clock);
            var c = new SteppedClock();
            var at = new PixelPoint(0, 0);
            ChaosCursorGlowOverlay.ReadPointer = () => (at, false, false);
            ChaosCursorGlowOverlay.MakeClickThrough = _ => clickThrough();
            ChaosCursorGlowOverlay.Clock = c;
            try { body(c, (x, y) => at = new PixelPoint(x, y)); }
            finally
            {
                ChaosCursorGlowOverlay.CloseActive();
                (ChaosCursorGlowOverlay.ReadPointer, ChaosCursorGlowOverlay.MakeClickThrough, ChaosCursorGlowOverlay.Clock) = (read, make, clock);
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Arm_follows_and_breathes_disarm_stops_tracking_panic_closes() => WithSeams(() => true, (clock, point) =>
    {
        point(500, 300);
        ChaosCursorGlowOverlay.Arm();
        var w = ChaosCursorGlowOverlay.Active!;
        Assert.True(w.IsVisible && w.HaloVisible && w.Tracking);
        Assert.False(w.IsHitTestVisible || w.ShowActivated || w.ShowInTaskbar);
        Assert.True(w.Topmost);
        Assert.Equal(76, w.Width);
        var half = (int)Math.Round(38 * w.RenderScaling);
        Assert.Equal(new PixelPoint(500 - half, 300 - half), w.Position);
        Assert.Equal(0.85, w.HaloScale, 6);

        clock.Now += TimeSpan.FromMilliseconds(620).Ticks;
        point(900, 100);
        w.Tick();
        Assert.Equal(1.12, w.HaloScale, 6);
        Assert.Equal(new PixelPoint(900 - half, 100 - half), w.Position);

        ChaosCursorGlowOverlay.Disarm();   // hidden: no timer, no follow
        Assert.False(w.Tracking || w.HaloVisible);
        point(10, 10);
        w.Tick();
        ChaosCursorGlowOverlay.MoveToPx(20, 20);
        Assert.Equal(new PixelPoint(900 - half, 100 - half), w.Position);

        ChaosCursorGlowOverlay.Arm();      // keep-alive: the same window comes back
        Assert.Same(w, ChaosCursorGlowOverlay.Active);
        Assert.True(w.Tracking);

        PanicSurfaces.All.Single(s => s.Id == "chaos-cursor-glow").Stop(null);
        Assert.Null(ChaosCursorGlowOverlay.Active);
        Assert.False(w.IsVisible || w.Tracking);
    });

    [Fact]
    public Task Chaos_teardown_closes_it_even_when_no_run_is_active() => WithSeams(() => true, (_, _) =>
    {
        ChaosCursorGlowOverlay.Arm();
        var w = ChaosCursorGlowOverlay.Active!;
        ChaosRunHost.ForceShutdown();
        Assert.Null(ChaosCursorGlowOverlay.Active);
        Assert.False(w.IsVisible);
    });

    [Fact]
    public Task A_platform_without_click_through_never_shows_it_and_is_asked_once_per_run()
    {
        int asked = 0;
        return WithSeams(() => { asked++; return false; }, (_, _) =>
        {
            ChaosCursorGlowOverlay.Arm();
            Assert.Null(ChaosCursorGlowOverlay.Active);
            ChaosCursorGlowOverlay.Arm();          // no second window churn after a refusal
            Assert.Equal(1, asked);
            ChaosCursorGlowOverlay.CloseActive();  // run teardown forgets it
            ChaosCursorGlowOverlay.Arm();
            Assert.Equal(2, asked);
        });
    }
}
