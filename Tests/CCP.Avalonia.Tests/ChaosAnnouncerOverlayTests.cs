using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
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
                .UseSkia().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Banner_replaces_text_and_renders_only_nonblank_uppercase_suffix(bool suffix)
    {
        await Run(_ =>
        {
            var key = "announcer-test-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(CorePaths.UserData, "assets", "assets", "Chaos", "announce", key + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96)))
                bitmap.Save(path);
            try
            {
                ChaosAnnouncerOverlay.Announce("fallback", ChaosAnnounceKind.Item, key, suffix ? "ten seconds" : " ");
                var host = Assert.IsType<Grid>(ChaosAnnouncerOverlay.Active!.Content);
                var anchor = Assert.IsType<Grid>(Assert.Single(host.Children));
                var panel = Assert.IsType<StackPanel>(Assert.Single(anchor.Children));
                var image = Assert.IsType<Image>(panel.Children[0]);
                Assert.NotNull(image.Source);
                Assert.Equal(120, image.Height);
                Assert.Equal(Stretch.Uniform, image.Stretch);
                Assert.Equal(suffix ? 2 : 1, panel.Children.Count);
                if (suffix)
                {
                    var label = Assert.IsType<OutlinedTextBlock>(panel.Children[1]);
                    Assert.Equal("TEN SECONDS", label.Text);
                    Assert.Equal(26, label.FontSize);
                    Assert.Equal(2.2, label.StrokeThickness);
                }
            }
            finally { File.Delete(path); }
        });
    }

    // The live art keys (ChaosRunHost T-10 s / relapse, ChaosOverlayWindow "it noticed.") resolve from the
    // shipped build output, not only from a user-data drop: CCP.Avalonia.csproj links WPF's announce PNGs.
    [Theory]
    [InlineData("ending_soon")]
    [InlineData("relapse")]
    [InlineData("it_noticed")]
    public async Task Live_art_keys_ship_with_the_head(string key)
    {
        await Run(_ =>
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", "announce", key + ".png")));
            Assert.NotNull(ChaosEffectBannerOverlay.ResolveArt(key));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_unreadable_art_uses_uppercase_text_without_suffix(bool corrupt)
    {
        await Run(_ =>
        {
            var key = "announcer-missing-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(CorePaths.UserData, "assets", "assets", "Chaos", "announce", key + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (corrupt) File.WriteAllText(path, "invalid image");
            try
            {
                ChaosAnnouncerOverlay.Announce("fallback", ChaosAnnounceKind.Item, key, "ignored");
                var host = Assert.IsType<Grid>(ChaosAnnouncerOverlay.Active!.Content);
                var anchor = Assert.IsType<Grid>(Assert.Single(host.Children));
                var label = Assert.IsType<OutlinedTextBlock>(Assert.Single(anchor.Children));
                Assert.Equal("FALLBACK", label.Text);
                Assert.Equal(60, label.FontSize);
            }
            finally { File.Delete(path); }
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
