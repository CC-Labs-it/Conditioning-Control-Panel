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

[Collection(RunsAloneCollection.Name)]
public sealed class FlashV2InteractionTests
{
    private static Task OnUi(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options(systemEmoji: false)).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
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
    public Task Motion_grants_expose_dragging_and_toggle_saves() => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => false;
        var unowned = new FlashFeatureControl();
        Assert.False(unowned.FindControl<Grid>("RowDraggable")!.IsVisible);
        foreach (var grant in new[] { PrizeOwnership.FlashDriftBounce, PrizeOwnership.FlashPendulum })
        {
            PrizeOwnership.IsGranted = id => id == grant;
            var card = new FlashFeatureControl();
            Assert.True(card.FindControl<Grid>("RowDraggable")!.IsVisible);
            card.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked = false;
            card.FindControl<CheckBox>("ChkFlashDraggable")!.IsChecked = true;
            Assert.True(CoreSettings.Current.FlashDraggable);
        }
    });

    // WPF FlashService.cs:1963 clips the picture. Until that renderer lands here, no editor
    // should promise rounding or overwrite a preference restored from the WPF head.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Rounded_editor_waits_for_its_renderer_and_preserves_the_synced_preference(bool rounded) => OnUi(() =>
    {
        CoreSettings.Current.FlashRoundedCorners = rounded;
        foreach (var grant in new[] { PrizeOwnership.FlashDriftBounce, PrizeOwnership.FlashPendulum })
        {
            PrizeOwnership.IsGranted = id => id == grant;
            var card = new FlashFeatureControl();
            Assert.Null(card.FindControl<Grid>("RowRoundedCorners"));
            Assert.Null(card.FindControl<CheckBox>("ChkFlashRoundedCorners"));
            Assert.Equal(rounded, CoreSettings.Current.FlashRoundedCorners);
        }
    });

    [Fact]
    public Task Accepted_ownership_snapshot_rebuilds_an_attached_card_without_saving_a_new_style() => OnUi(() =>
    {
        var s = CoreSettings.Current;
        // AuthToken lives in CoreSecrets: without an in-memory store the identity reads no token.
        var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new System.Collections.Generic.Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var old = (s.OfflineMode, s.UnifiedId, s.FlashMotionStyle);
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
            PrizeOwnership.ApplySnapshot("other-account", 1, new[] { PrizeOwnership.FlashDriftBounce });
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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
            (s.OfflineMode, s.UnifiedId, s.FlashMotionStyle) = old;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
        }
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
    public Task Gentle_visuals_caps_a_throw_and_capture_loss_cancels_without_popping() => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => true;
        CoreSettings.Current.FlashDraggable = true;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.GentleVisuals = true;
        var w = Shown();
        var m = FlashOverlay.BeginDrag(w, new PixelPoint(150, 150), 1000)!;
        FlashDrag.Sample(m.Drag!, 350, 250, 1200);
        FlashOverlay.EndDrag(w, m, new PixelPoint(450, 300), 1250);
        Assert.Equal(1000, m.Vx, 6);
        Assert.Equal(500, m.Vy, 6);
        FlashOverlay.CloseAll(final: false);
        var held = Shown();
        held.MouseDown(new Point(50, 50), MouseButton.Left);
        var state = FlashOverlay.Drifting.Single(e => e.Window == held).Motion;
        held.RaiseEvent(new PointerCaptureLostEventArgs(held, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true)));
        Assert.Null(state.Drag);
        Assert.Equal(0, state.Vx);
        Assert.Contains(FlashOverlay.Active, e => e.Window == held);
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
        // A placed Still flash never moves again: the next tick drops it and stops the 16 ms clock.
        FlashOverlay.DriftTick();
        Assert.DoesNotContain(FlashOverlay.Drifting, e => e.Window == w);
        Assert.False(FlashOverlay.DriftRunning);
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
