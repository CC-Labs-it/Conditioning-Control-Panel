using System;
using System.Linq;
using System.IO;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashFinishTests
{
    private static Task OnUi(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        body();
        return Task.CompletedTask;
    });

    [Fact]
    public void Owned_pendulum_plays_and_unowned_or_motion_off_stays_still()
    {
        var was = PrizeOwnership.IsGranted;
        try
        {
            var s = new AppSettings { FlashMotionStyle = FlashMotionStyle.Pendulum, MotionLevel = MotionLevel.Full };
            var rect = new PixelRect(200, 250, 400, 300);
            var screen = new PixelRect(0, 0, 1920, 1080);
            PrizeOwnership.IsGranted = id => id == PrizeGrants.FlashPendulum;
            Assert.Equal(FlashMotionStyle.Pendulum, FlashOverlay.BuildMotion(rect, screen, s, new Random(3))?.Style);
            s.MotionLevel = MotionLevel.Off;
            Assert.Null(FlashOverlay.BuildMotion(rect, screen, s, new Random(3)));
            s.MotionLevel = MotionLevel.Full;
            PrizeOwnership.IsGranted = _ => false;
            Assert.Null(FlashOverlay.BuildMotion(rect, screen, s, new Random(3)));
        }
        finally { PrizeOwnership.IsGranted = was; }
    }

    [Fact]
    public Task Flash_card_paints_both_feature_plates() => OnUi(() =>
    {
        var card = new FlashFeatureControl();
        var host = new Window { Content = card };
        try
        {
            host.Show();
            Assert.IsType<ImageBrush>(card.FindControl<Border>("HeroArt")?.Background);
            Assert.IsType<ImageBrush>(card.FindControl<Border>("SideArt")?.Background);
        }
        finally { host.Close(); }
    });
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static FlashOverlayWindow ShowOne()
    {
        var screen = new Window().Screens.All[0];
        var pic = new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));
        Assert.True(FlashOverlay.Show(pic, new PixelRect(100, 100, 400, 300), "sample.png", screen,
            1, TimeSpan.Zero, TimeSpan.FromSeconds(6), true, false, 0));
        return FlashOverlay.Active[^1].Window;
    }

    [Fact]
    public Task Rounded_and_drag_options_follow_ownership_and_rebind_without_saving_over_the_pick() => OnUi(() =>
    {
        var s = CoreSettings.Current;
        var saved = (PrizeOwnership.IsGranted, s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashDraggable);
        var host = new Window();
        try
        {
            PrizeOwnership.IsGranted = id => id == PrizeGrants.FlashPendulum;
            s.FlashMotionStyle = FlashMotionStyle.Pendulum;
            var card = new FlashFeatureControl();
            host.Content = card;
            host.Show();
            var cmb = card.FindControl<ComboBox>("CmbMotion")!;
            Assert.Equal(new[] { FlashMotionStyle.Still, FlashMotionStyle.Pendulum, FlashMotionStyle.Mix },
                cmb.Items.OfType<ComboBoxItem>().Select(x => (FlashMotionStyle)x.Tag!));
            card.FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked = true;
            card.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked = true;
            Assert.True(s.FlashRoundedCorners && s.FlashDraggable);
            PrizeOwnership.IsGranted = _ => false;
            PrizeGrants.RaiseGrantsChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.FindControl<Grid>("RowRoundedCorners")!.IsVisible);
            Assert.False(card.FindControl<Grid>("RowDraggable")!.IsVisible);
            Assert.Equal(FlashMotionStyle.Still, ((ComboBoxItem)cmb.SelectedItem!).Tag);
            Assert.Equal(FlashMotionStyle.Pendulum, s.FlashMotionStyle); // refreshing cannot rewrite synced settings
        }
        finally
        {
            host.Close();
            (PrizeOwnership.IsGranted, s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashDraggable) = saved;
        }
    });

    [Fact]
    public Task Rounded_picture_uses_the_core_radius_and_dragging_moves_it_before_a_tap_can_pop() => OnUi(() =>
    {
        var s = CoreSettings.Current;
        var saved = (PrizeOwnership.IsGranted, FlashOverlay.Present, FlashOverlay.Clock,
            s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashDraggable, s.FlashExitStyle, s.CorruptionMode, s.MotionLevel);
        var clock = new Clock();
        try
        {
            PrizeOwnership.IsGranted = _ => true;
            FlashOverlay.Present = (_, _, _) => true;
            FlashOverlay.Clock = clock;
            (s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashDraggable, s.FlashExitStyle, s.CorruptionMode, s.MotionLevel) =
                (FlashMotionStyle.Still, true, true, FlashExitStyle.None, false, MotionLevel.Full);
            var w = ShowOne();
            var image = ((Panel)((Panel)w.Content!).Children[0]).Children.OfType<Image>().Single();
            Assert.Equal(FlashCorners.Resolve(true, true, false, 300, 1), ((RectangleGeometry)image.Clip!).RadiusX);
            w.MouseDown(new Point(50, 50), MouseButton.Left);
            Assert.Contains(FlashOverlay.Active, x => x.Window == w); // a draggable press grabs rather than pops
            clock.Now += TimeSpan.TicksPerSecond / 10;
            w.MouseMove(new Point(100, 80));
            FlashOverlay.DriftTick();
            Assert.NotEqual(new PixelPoint(100, 100), w.Position);
            w.MouseUp(new Point(100, 80), MouseButton.Left);
            var m = FlashOverlay.Drifting.Single(x => x.Window == w).Motion;
            Assert.True(m.Drag?.Flinging);
            w.Close();
            var tap = ShowOne();
            tap.MouseDown(new Point(50, 50), MouseButton.Left);
            tap.MouseUp(new Point(50, 50), MouseButton.Left);
            Assert.DoesNotContain(FlashOverlay.Active, x => x.Window == tap);
        }
        finally
        {
            FlashOverlay.CloseAll(false);
            (PrizeOwnership.IsGranted, FlashOverlay.Present, FlashOverlay.Clock,
                s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashDraggable, s.FlashExitStyle, s.CorruptionMode, s.MotionLevel) = saved;
        }
    });

    [Fact]
    public Task Pendulum_tick_rotates_the_picture_inside_its_live_bounds_and_closing_stops_the_clock() => OnUi(() =>
    {
        var was = FlashOverlay.Clock;
        var clock = new Clock();
        FlashOverlayWindow? w = null;
        try
        {
            FlashOverlay.Clock = clock;
            w = new FlashOverlayWindow();
            w.Show();
            var m = FlashMotion.Create(FlashMotionStyle.Pendulum, 200, 250, 400, 300,
                0, 0, 1920, 1080, MotionLevel.Full, new Random(3));
            FlashOverlay.StartDrift(w, m);
            clock.Now += TimeSpan.TicksPerSecond / 10;
            FlashOverlay.DriftTick();
            Assert.Equal(m.AngleRad * 180 / Math.PI, ((RotateTransform)w.PictureTransform!).Angle, 6);
            Assert.Equal(m.W, w.Width, 6);
            Assert.Equal(m.H, w.Height, 6);
            Assert.Equal(new PixelPoint((int)Math.Round(m.X), (int)Math.Round(m.Y)), w.Position);
            w.Close();
            Assert.False(FlashOverlay.DriftRunning);
        }
        finally { w?.Close(); FlashOverlay.Clock = was; }
    });

    // Same 8x8 restore-previous fixture as SpiralOverlayTests: all three frames must survive.
    private const string Gif = "R0lGODlhCAAIAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEAwAAACwAAAAACAAIAAAIDwABCBxIsKDBgwgTKkwYEAAh+QQNAwACACwAAAAAAgACAIEA/wD/AAAAAAAAAAAIBgABCAQQEAAh+QQFAwACACwAAAAAAgACAIH/AAAAAP8AAAAAAAAIBgADABgYEAA7";

    [Fact]
    public Task Gif_decodes_all_frames_scales_speed_and_loops_on_elapsed_time() => OnUi(() =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-flash-frames-");
        var path = Path.Combine(dir.FullName, "sample.gif");
        var was = (FlashOverlay.Clock, FlashOverlay.Present);
        var clock = new Clock();
        FlashOverlayWindow? w = null;
        try
        {
            FlashOverlay.Clock = clock;
            FlashOverlay.Present = (_, _, _) => true;
            File.WriteAllBytes(path, Convert.FromBase64String(Gif));
            var picture = FlashPicture.Decode(path, 4, 2);
            Assert.Equal(3, picture.Frames.Count);
            Assert.Equal(new PixelSize(4, 4), picture.Frames[0].PixelSize);
            Assert.Equal(TimeSpan.FromMilliseconds(15), picture.Delay);
            var screen = new Window().Screens.All[0];
            Assert.True(FlashOverlay.Show(picture, new PixelRect(100, 100, 100, 100), path, screen, 1,
                TimeSpan.Zero, TimeSpan.FromSeconds(6), false, false, 0, null));
            w = FlashOverlay.Active[^1].Window;
            Assert.True(FlashOverlay.DriftRunning); // GIFs use the shared clock even with no motion
            clock.Now += picture.Delay.Ticks * 2;
            FlashOverlay.DriftTick();
            Assert.Equal(2, w.FrameIndex);
            clock.Now += picture.Delay.Ticks;
            w.AdvanceFrame();
            Assert.Equal(0, w.FrameIndex);
            w.Close();
            clock.Now += picture.Delay.Ticks;
            w.AdvanceFrame(); // closed windows never reuse disposed frames
            Assert.Equal(0, w.FrameIndex);
        }
        finally { w?.Close(); (FlashOverlay.Clock, FlashOverlay.Present) = was; dir.Delete(true); }
    });

    [Theory]
    [InlineData(100, 2, 50)]
    [InlineData(0, 1, 100)]
    [InlineData(20, 4, 10)]
    [InlineData(100, double.NaN, 100)]
    public void Gif_speed_matches_wpf_delay_floor_and_invalid_input_fallback(double ms, double speed, double expected) =>
        Assert.Equal(expected, FlashPicture.ScaleDelay(TimeSpan.FromMilliseconds(ms), speed).TotalMilliseconds);

    [Fact]
    public Task Stop_closes_point_fired_flashes_even_when_the_scheduler_is_idle_and_panic_stops_the_shared_clock() => OnUi(() =>
    {
        var saved = (CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider,
            CoreFlash.StopProvider, FlashOverlay.Present);
        try
        {
            FlashOverlay.Present = (_, _, _) => true;
            FlashOverlay.SeedCore(() => null, () => { });
            var w = ShowOne();
            var generation = FlashOverlay.Generation;
            CoreFlash.Stop();
            Assert.Empty(FlashOverlay.Active);
            Assert.True(FlashOverlay.Generation > generation); // queued decodes and spawns are orphaned too
            var again = ShowOne();
            FlashOverlay.StartDrift(again, FlashMotion.Create(FlashMotionStyle.DriftBounce,
                100, 100, 400, 300, 0, 0, 1920, 1080, MotionLevel.Full, new Random(3)));
            PanicSurfaces.All.Single(x => x.Id == "engine").Stop(null);
            Assert.Empty(FlashOverlay.Active);
            Assert.False(FlashOverlay.DriftRunning);
        }
        finally
        {
            FlashOverlay.CloseAll(false);
            (CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider,
                CoreFlash.StopProvider, FlashOverlay.Present) = saved;
        }
    });

}
