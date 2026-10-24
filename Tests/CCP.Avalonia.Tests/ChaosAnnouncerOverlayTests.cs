using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ChaosAnnouncerOverlay on a stepped clock: lines queue and play in order through the
/// Core envelope, the window idles hidden, a run's T-10 s beat announces, and panic drops it all.</summary>
public sealed class ChaosAnnouncerOverlayTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public void Advance(int ms) => Ticks += TimeSpan.FromMilliseconds(ms).Ticks;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static async Task Run(Action<SteppedClock> body)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            bool was = s.ChaosAnnouncerEnabled;
            var clock = new SteppedClock();
            ChaosAnnouncerOverlay.Clock = clock;
            s.ChaosAnnouncerEnabled = true;
            try { body(clock); }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosAnnouncerOverlay.CloseActive();
                ChaosAnnouncerOverlay.Clock = TimeProvider.System;
                s.ChaosAnnouncerEnabled = was;
            }
            return Task.CompletedTask;
        });
    }

    private static void Step(SteppedClock c, int ms) { c.Advance(ms); ChaosAnnouncerOverlay.Active?.Step(); }

    [Fact]
    public async Task Lines_queue_and_play_in_order_then_the_window_idles_hidden()
    {
        await Run(clock =>
        {
            ChaosAnnouncerOverlay.Announce("first", ChaosAnnounceKind.Mantra);
            ChaosAnnouncerOverlay.Announce("second", ChaosAnnounceKind.Item, holdMs: ChaosAnnounceTiming.TeachHoldMs);
            var w = ChaosAnnouncerOverlay.Active!;
            Assert.True(w.IsVisible);
            Assert.Equal("first", w.Current!.Text);
            Assert.Equal(1, ChaosAnnouncerOverlay.QueuedCount);
            Assert.Equal(0.85, w.ContentScale, 3);

            Step(clock, 55);
            Assert.Equal(0.5, w.ContentOpacity, 2);
            Step(clock, 944);   // 999 ms: still fading out
            Assert.Equal("first", w.Current!.Text);
            Step(clock, 1);     // 1000 ms = IN + HOLD + OUT: the next line takes over the same window
            Assert.Same(w, ChaosAnnouncerOverlay.Active);
            Assert.Equal("second", w.Current!.Text);
            Assert.True(w.IsVisible);

            Step(clock, ChaosAnnounceTiming.TotalMs(ChaosAnnounceTiming.TeachHoldMs));
            Assert.Null(w.Current);
            Assert.False(w.IsVisible);   // idle: hidden, kept alive
            Assert.False(ChaosAnnouncerOverlay.IsShowing);
            Assert.Same(w, ChaosAnnouncerOverlay.Active);
        });
    }

    [Fact]
    public async Task Disabled_or_blank_announces_nothing()
    {
        await Run(_ =>
        {
            ChaosAnnouncerOverlay.Announce("  ", ChaosAnnounceKind.Depth);
            CoreSettings.Current.ChaosAnnouncerEnabled = false;
            ChaosAnnouncerOverlay.Announce("hello", ChaosAnnounceKind.Depth);
            Assert.Null(ChaosAnnouncerOverlay.Active);
            Assert.False(ChaosAnnouncerOverlay.IsShowing);
        });
    }

    [Fact]
    public async Task Panic_drops_the_queue_and_closes_the_window()
    {
        await Run(_ =>
        {
            ChaosAnnouncerOverlay.Announce("one", ChaosAnnounceKind.Streak);
            ChaosAnnouncerOverlay.Announce("two", ChaosAnnounceKind.Streak);
            var w = ChaosAnnouncerOverlay.Active!;
            PanicSurfaces.All.Single(x => x.Id == "chaos-announcer").Stop(null);
            Assert.Null(ChaosAnnouncerOverlay.Active);
            Assert.Equal(0, ChaosAnnouncerOverlay.QueuedCount);
            Assert.False(ChaosAnnouncerOverlay.IsShowing);
            Assert.False(w.IsVisible);
        });
    }

    [Fact]
    public async Task A_run_announces_its_last_ten_seconds_once_and_teardown_closes_it()
    {
        await Run(_ =>
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            for (int i = 0; i < 4 * 49; i++) ChaosRunHost.Tick();   // 49 s
            Assert.Null(ChaosAnnouncerOverlay.Active);
            for (int i = 0; i < 8; i++) ChaosRunHost.Tick();         // 51 s: inside T-10 s
            var w = ChaosAnnouncerOverlay.Active!;
            Assert.Equal("the hole is closing…", w.Current!.Text);
            Assert.Equal(ChaosAnnounceKind.Depth, w.Current.Kind);
            Assert.Equal(0, ChaosAnnouncerOverlay.QueuedCount);       // once per run, not every tick

            ChaosRunHost.ForceShutdown();
            Assert.Null(ChaosAnnouncerOverlay.Active);
            Assert.False(w.IsVisible);
        });
    }
    [Fact]
    public void Line_is_anchored_to_the_work_area_including_a_left_docked_panel()
    {
        // 1.5x screen, 96 px panel docked left, 48 px top bar (WPF adds wa.Left, ChaosAnnouncerOverlay.cs:244).
        var (left, top, width) = ChaosAnnouncerOverlay.WorkAreaAnchor(
            new PixelRect(0, 0, 1920, 1080), new PixelRect(96, 48, 1824, 1032), 1.5);
        Assert.Equal(64, left);
        Assert.Equal(32, top);
        Assert.Equal(1216, width);
    }
}
