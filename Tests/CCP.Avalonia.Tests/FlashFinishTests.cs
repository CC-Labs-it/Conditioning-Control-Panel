using System;
using System.Linq;
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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia()
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
            s.FlashRoundedCorners = s.FlashDraggable = false;
            var card = new FlashFeatureControl();
            host.Content = card;
            host.Show();
            var cmb = card.FindControl<ComboBox>("CmbMotion")!;
            Assert.Equal(new[] { FlashMotionStyle.Still, FlashMotionStyle.Pendulum, FlashMotionStyle.Mix },
                cmb.Items.OfType<ComboBoxItem>().Select(x => (FlashMotionStyle)x.Tag!));
            card.FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked = true;
            card.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked = true;
            Assert.True(s.FlashRoundedCorners && s.FlashDraggable);
            Dispatcher.UIThread.RunJobs(); // drain settings refreshes before testing the grant event
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
            var busy = typeof(FlashOverlay).GetField("_busy", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            busy.SetValue(null, true);
            CoreFlash.Stop();
            Assert.False(FlashOverlay.IsBusy);
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

    [Fact]
    public Task Retired_spawns_cannot_complete_a_new_burst() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        await OnUi(() => { });
        var was = FlashOverlay.Present;
        var staleDone = false;
        var done = new TaskCompletionSource();
        try
        {
            FlashOverlay.Present = (_, _, _) => true;
            var screen = new Window().Screens.All[0];
            Bitmap Picture() => new(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));
            var rect = new PixelRect(100, 100, 100, 100);
            var old = new System.Collections.Generic.List<(Bitmap, PixelRect, string, Screen)>
                { (Picture(), rect, "old.png", screen) };
            FlashOverlay.Schedule(old, 1, 0, FlashOverlay.Generation, 40, 1, TimeSpan.Zero,
                TimeSpan.FromSeconds(6), false, false, 0, () => staleDone = true);
            FlashOverlay.CloseAll(false);
            var next = new System.Collections.Generic.List<(Bitmap, PixelRect, string, Screen)>
                { (Picture(), rect, "next.png", screen) };
            FlashOverlay.Schedule(next, 30, 0, FlashOverlay.Generation, 40, 1, TimeSpan.Zero,
                TimeSpan.FromSeconds(6), false, false, 0, () => done.SetResult());
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(staleDone);
            Assert.Single(FlashOverlay.Active);
        }
        finally { FlashOverlay.CloseAll(false); FlashOverlay.Present = was; }
    });

    [Fact]
    public Task Pendulum_spacing_uses_only_live_neighbours_on_the_same_monitor() => OnUi(() =>
    {
        var was = PrizeOwnership.IsGranted;
        var peer = new Window();
        try
        {
            PrizeOwnership.IsGranted = _ => true;
            var s = new AppSettings { FlashMotionStyle = FlashMotionStyle.Pendulum, MotionLevel = MotionLevel.Full };
            var rect = new PixelRect(200, 250, 400, 300);
            var screen = new PixelRect(0, 0, 1920, 1080);
            var m = FlashOverlay.BuildMotion(rect, screen, s, new Random(3))!;
            FlashOverlay.Drifting.Add((peer, m));
            var next = FlashOverlay.BuildMotion(rect, screen, s, new Random(3))!;
            Assert.NotEqual(m.PivotX, next.PivotX);
            m.BoundsX += 1920;
            var otherScreen = FlashOverlay.BuildMotion(rect, screen, s, new Random(3))!;
            Assert.Equal(m.PivotX, otherScreen.PivotX);
            Assert.Equal(m.Phase, otherScreen.Phase);
        }
        finally { FlashOverlay.Drifting.RemoveAll(x => x.Window == peer); PrizeOwnership.IsGranted = was; }
    });

}
