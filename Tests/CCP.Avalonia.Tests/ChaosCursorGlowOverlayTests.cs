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
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;

namespace CCP.Avalonia.Tests;

/// <summary>Rabbit Caller's cursor halo (WPF Chaos/ChaosCursorGlowOverlay.cs): Arm shows it and follows
/// the pointer with the 0.85..1.12 breath, Disarm stops tracking, panic and run teardown close it, and a
/// platform that cannot make it click-through never shows it.</summary>
public sealed class ChaosCursorGlowOverlayTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public Task Run_precreates_only_the_equipped_classic_halo(bool equipped, bool skia) => WithSeams(() => true, (_, _) =>
    {
        var meta = ChaosMeta.State;
        bool wasEquipped = meta.ActiveLifetimeBoons.Contains("rabbit_caller");
        bool hadLevel = meta.LifetimeBoonLevels.TryGetValue("rabbit_caller", out int level);
        bool wasSkia = CoreSettings.Current.ChaosSkiaFxEnabled;
        try
        {
            if (equipped) meta.ActiveLifetimeBoons.Add("rabbit_caller");
            else meta.ActiveLifetimeBoons.Remove("rabbit_caller");
            meta.LifetimeBoonLevels["rabbit_caller"] = 1;
            CoreSettings.Current.ChaosSkiaFxEnabled = skia;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            Assert.Null(ChaosCursorGlowOverlay.Active); // countdown has not completed
            ChaosRunHost.Overlay!.FinishCountdown();
            Assert.Equal(equipped && !skia, ChaosCursorGlowOverlay.Active != null);
            if (ChaosCursorGlowOverlay.Active is { } w)
            {
                Assert.True(w.IsVisible);
                Assert.False(w.HaloVisible || w.Tracking);
                Assert.Equal(new PixelPoint((int)(-152 * w.RenderScaling), (int)(-152 * w.RenderScaling)), w.Position);
                ChaosCursorGlowOverlay.EnsureCreated();
                Assert.Same(w, ChaosCursorGlowOverlay.Active);
            }
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            if (wasEquipped) meta.ActiveLifetimeBoons.Add("rabbit_caller");
            else meta.ActiveLifetimeBoons.Remove("rabbit_caller");
            if (hadLevel) meta.LifetimeBoonLevels["rabbit_caller"] = level;
            else meta.LifetimeBoonLevels.Remove("rabbit_caller");
            CoreSettings.Current.ChaosSkiaFxEnabled = wasSkia;
        }
        Assert.Null(ChaosCursorGlowOverlay.Active);
    });

    [Fact]
    public Task Every_arm_restacks_the_same_precreated_window() => WithSeams(() => true, (_, _) =>
    {
        var raise = ChaosCursorGlowOverlay.Raise;
        try
        {
            int raised = 0;
            ChaosCursorGlowOverlay.Raise = w =>
            {
                Assert.Same(ChaosCursorGlowOverlay.Active, w);
                Assert.True(ChaosCursorGlowOverlay.Active!.HaloVisible);
                raised++;
            };
            ChaosCursorGlowOverlay.EnsureCreated();
            var retained = ChaosCursorGlowOverlay.Active;
            Assert.NotNull(retained);
            Assert.False(retained.HaloVisible || retained.Tracking);
            ChaosCursorGlowOverlay.EnsureCreated();
            Assert.Same(retained, ChaosCursorGlowOverlay.Active);
            Assert.Equal(0, raised);
            ChaosCursorGlowOverlay.Arm();
            ChaosCursorGlowOverlay.Disarm();
            ChaosCursorGlowOverlay.Arm();
            Assert.Same(retained, ChaosCursorGlowOverlay.Active);
            Assert.Equal(2, raised);
        }
        finally { ChaosCursorGlowOverlay.Raise = raise; }
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
