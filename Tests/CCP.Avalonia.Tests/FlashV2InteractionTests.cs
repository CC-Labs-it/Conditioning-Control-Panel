using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashV2InteractionTests
{
    private static Task OnUi(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.FlashDraggable, s.FlashRoundedCorners, s.FlashExitStyle, s.MotionLevel, PrizeOwnership.IsGranted);
        try { body(); }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            (s.FlashDraggable, s.FlashRoundedCorners, s.FlashExitStyle, s.MotionLevel, PrizeOwnership.IsGranted) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Motion_grants_expose_rounding_and_dragging_and_toggles_save() => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => false;
        var unowned = new FlashFeatureControl();
        Assert.False(unowned.FindControl<Grid>("RowRoundedCorners")!.IsVisible);
        Assert.False(unowned.FindControl<Grid>("RowDraggable")!.IsVisible);
        foreach (var grant in new[] { PrizeOwnership.FlashDriftBounce, PrizeOwnership.FlashPendulum })
        {
            PrizeOwnership.IsGranted = id => id == grant;
            var card = new FlashFeatureControl();
            Assert.True(card.FindControl<Grid>("RowRoundedCorners")!.IsVisible);
            Assert.True(card.FindControl<Grid>("RowDraggable")!.IsVisible);
            card.FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked = false;
            card.FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked = true;
            Assert.True(CoreSettings.Current.FlashRoundedCorners);
            card.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked = false;
            card.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked = true;
            Assert.True(CoreSettings.Current.FlashDraggable);
        }
    });

    [Fact]
    public Task Accepted_ownership_snapshot_rebuilds_an_attached_card_without_saving_a_new_style() => OnUi(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.OfflineMode, s.UnifiedId, s.AuthToken, s.FlashMotionStyle);
        Window? host = null;
        try
        {
            s.OfflineMode = false; s.UnifiedId = "test-account"; s.AuthToken = "test-token";
            s.FlashMotionStyle = FlashMotionStyle.DriftBounce;
            PrizeOwnership.ResetHeld();
            PrizeOwnership.IsGranted = PrizeOwnership.Held;
            var card = new FlashFeatureControl();
            host = new Window { Content = card };
            host.Show();
            Assert.False(card.FindControl<Grid>("RowDraggable")!.IsVisible);
            PrizeOwnership.ApplySnapshot("test-account", 1, new[] { PrizeOwnership.FlashDriftBounce });
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(card.FindControl<Grid>("RowDraggable")!.IsVisible);
            PrizeOwnership.ApplySnapshot("test-account", 2, Array.Empty<string>());
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(card.FindControl<Grid>("RowDraggable")!.IsVisible);
            Assert.Equal(FlashMotionStyle.DriftBounce, s.FlashMotionStyle);
        }
        finally
        {
            host?.Close();
            PrizeOwnership.ResetHeld();
            (s.OfflineMode, s.UnifiedId, s.AuthToken, s.FlashMotionStyle) = old;
        }
    });

    [Theory]
    [InlineData(true, true, false, 7)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, true, 12)]
    public Task Picture_clip_uses_core_radius_and_keeps_the_glow_outside(bool rounded, bool owned, bool glow, double radius) => OnUi(() =>
    {
        var w = new FlashOverlayWindow();
        try
        {
            w.ApplyCorners(rounded, owned, glow, 200, 100);
            if (radius == 0) Assert.Null(w.PictureClip);
            else
            {
                var clip = Assert.IsType<RectangleGeometry>(w.PictureClip);
                Assert.Equal(radius, clip.RadiusX, 6);
                Assert.Equal(new Rect(0, 0, 200, 100), clip.Rect);
            }
        }
        finally { w.Close(); }
    });

    private static FlashOverlayWindow Shown()
    {
        var w = new FlashOverlayWindow();
        w.MakeClickable(() => FlashOverlay.Pop(w));
        FlashOverlay.Active.Add((w, new PixelRect(100, 100, 200, 100)));
        w.Closed += (_, _) => FlashOverlay.Active.RemoveAll(e => e.Window == w);
        w.Show();
        return w;
    }

    [Fact]
    public Task Left_grab_waits_for_release_and_right_press_still_pops() => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => true;
        CoreSettings.Current.FlashDraggable = true;
        CoreSettings.Current.FlashExitStyle = FlashExitStyle.None;
        var w = Shown();
        w.MouseDown(new Point(50, 50), MouseButton.Left);
        Assert.Contains(FlashOverlay.Active, e => e.Window == w);
        Assert.True(FlashOverlay.Drifting.Single(e => e.Window == w).Motion.Drag!.Held);
        w.MouseUp(new Point(50, 50), MouseButton.Left);
        Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == w);
        Shown().MouseDown(new Point(50, 50), MouseButton.Right);
        Assert.Empty(FlashOverlay.Active);
        var held = Shown();
        held.MouseDown(new Point(50, 50), MouseButton.Left);
        held.MouseDown(new Point(50, 50), MouseButton.Right); // right is still the escape while left is held
        Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == held);
    });

    [Theory]
    [InlineData(MotionLevel.Full, FlashDragOutcome.Fling)]
    [InlineData(MotionLevel.Off, FlashDragOutcome.Place)]
    public Task Release_moves_the_live_rect_and_respects_motion_level(MotionLevel level, FlashDragOutcome expected) => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => true;
        var s = CoreSettings.Current;
        s.FlashDraggable = true;
        s.MotionLevel = level;
        var w = Shown();
        var m = FlashOverlay.BeginDrag(w, new PixelPoint(150, 150), 1000)!;
        Assert.NotNull(m);
        Assert.Null(FlashOverlay.BeginDrag(w, new PixelPoint(150, 150), 1010)); // one grab at a time
        FlashDrag.Sample(m.Drag!, 350, 250, 1200);
        var outcome = FlashOverlay.EndDrag(w, m, new PixelPoint(450, 300), 1250);
        Assert.Equal(expected, outcome);
        Assert.Equal(new PixelPoint(400, 250), w.Position);
        Assert.Equal(400, FlashOverlay.Active.Single(e => e.Window == w).Rect.X);
        Assert.Contains(FlashOverlay.Active, e => e.Window == w); // no pop/hydra for a drag
        FlashOverlay.CloseAll(final: false); // same panic route; held/flung windows and clock disappear
        Assert.Empty(FlashOverlay.Drifting);
        Assert.False(FlashOverlay.DriftRunning);
    });

    [Fact]
    public Task A_paused_release_places_without_inheriting_old_velocity() => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => true;
        CoreSettings.Current.FlashDraggable = true;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var w = Shown();
        var m = FlashOverlay.BeginDrag(w, new PixelPoint(150, 150), 1000)!;
        FlashDrag.Sample(m.Drag!, 350, 250, 1100);
        Assert.Equal(FlashDragOutcome.Place, FlashOverlay.EndDrag(w, m, new PixelPoint(350, 250), 1500));
        Assert.Equal(0, m.Vx);
        Assert.Null(m.Drag);
        Assert.Contains(FlashOverlay.Active, e => e.Window == w);
    });

    [Fact]
    public Task Drag_setting_and_ownership_are_read_on_press_for_existing_flashes() => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => false;
        CoreSettings.Current.FlashDraggable = true;
        var w = Shown();
        Assert.Null(FlashOverlay.BeginDrag(w, new PixelPoint(150, 150), 0));
        PrizeOwnership.IsGranted = _ => true;
        CoreSettings.Current.FlashDraggable = false;
        Assert.Null(FlashOverlay.BeginDrag(w, new PixelPoint(150, 150), 0));
        CoreSettings.Current.FlashDraggable = true;
        Assert.NotNull(FlashOverlay.BeginDrag(w, new PixelPoint(150, 150), 0));
    });
}
