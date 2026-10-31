using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Haptics.Core;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Click to pop and stay until popped on this head (WPF FlashService OnFlashClicked,
/// BuildExit and FlashStayRule): a press pops a clickable flash with its FlashExit leave, the
/// stay rule sets the 10-minute lifetime and the 40 cap, and Stop closes a flash mid-leave.</summary>
public sealed class FlashPopTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    /// <summary>One shown flash recorded the way Spawn records it, clickable or not.</summary>
    private static FlashOverlayWindow Shown(bool clickable)
    {
        var w = new FlashOverlayWindow();
        if (clickable) w.MakeClickable(() => FlashOverlay.Pop(w));
        FlashOverlay.Active.Add((w, new PixelRect(0, 0, 400, 300)));
        w.Closed += (_, _) => FlashOverlay.Active.RemoveAll(e => e.Window == w);
        w.Show();
        return w;
    }

    private static Task OnUi(Action<AppSettings, SteppedClock> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var (exit, motion, old) = (s.FlashExitStyle, s.MotionLevel, FlashOverlay.Clock);
        var clock = new SteppedClock();
        FlashOverlay.Clock = clock;
        try { body(s, clock); }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            (s.FlashExitStyle, s.MotionLevel, FlashOverlay.Clock) = (exit, motion, old);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_click_pops_the_flash_with_its_exit_animation_then_closes() => OnUi((s, clock) =>
    {
        s.FlashExitStyle = FlashExitStyle.Pop;
        s.MotionLevel = MotionLevel.Full;
        var w = Shown(clickable: true);
        var closed = false;
        w.Closed += (_, _) => closed = true;

        w.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == w);   // off the cap at once, like WPF
        Assert.Contains(w, FlashOverlay.Exiting);
        Assert.Equal(FlashExitStyle.Pop, w.Exit!.Style);
        Assert.False(closed);

        clock.Now += TimeSpan.TicksPerSecond / 10;   // 0.1 s of a 0.22 s Pop
        w.ExitFrame();
        var scale = ((TransformGroup)w.PictureTransform!).Children.OfType<ScaleTransform>().Single();
        Assert.Equal(FlashExit.Sample(w.Exit).ScaleX, scale.ScaleX, 6);
        Assert.True(scale.ScaleX > 1);   // the swell
        Assert.False(closed);

        clock.Now += TimeSpan.TicksPerSecond;
        w.ExitFrame();
        Assert.True(closed);
        Assert.Empty(FlashOverlay.Exiting);
    });

    [Fact]
    public Task A_pop_buzzes_the_flash_click_haptic() => OnUi((s, _) =>
    {
        var hs = new HapticSettings { Enabled = true, FlashClickEnabled = true };
        hs.EnsureV2Migrated();
        hs.V2.Provider("lovense").Enabled = false;
        hs.V2.Provider("mock").Enabled = true;
        hs.V2.Rule(HapticEventKind.FlashClick).Enabled = true;
        var haptics = new HapticService(hs);
        var posted = new System.Collections.Generic.List<string>();
        haptics.HapticTriggered += (_, label) => posted.Add(label);
        var old = CoreHaptics.Service;
        try
        {
            CoreHaptics.Service = haptics;
            s.FlashExitStyle = FlashExitStyle.None;
            Shown(clickable: true).MouseDown(new Point(200, 150), MouseButton.Left);
            Assert.Contains(posted, p => p.StartsWith("Flash Click", StringComparison.Ordinal));
        }
        finally
        {
            CoreHaptics.Service = old;
            haptics.Dispose();
        }
    });

    [Fact]
    public Task A_leaving_flash_grows_its_window_so_the_exit_is_not_clipped_and_the_picture_stays_put() => OnUi((s, _) =>
    {
        s.FlashExitStyle = FlashExitStyle.Spiral;
        s.MotionLevel = MotionLevel.Full;
        var w = Shown(clickable: true);
        w.Position = new PixelPoint(500, 400);
        w.UpdateLayout();
        var picture = w.GetVisualDescendants().OfType<Image>().Single();
        var before = picture.TranslatePoint(new Point(0, 0), w)!.Value + new Point(w.Position.X, w.Position.Y);
        var (size, ww, wh) = (picture.Bounds.Size, w.Bounds.Width, w.Bounds.Height);
        Assert.True(size.Width > 0 && size.Height > 0);

        w.MouseDown(new Point(200, 150), MouseButton.Left);
        w.UpdateLayout();
        var side = Math.Max(size.Width, size.Height) / 2;   // half the picture's longer side round, 1.1x its height below
        Assert.Equal((ww + 2 * side, wh + 2 * side + size.Height * 1.1), (w.Width, w.Height));
        Assert.Equal(size, picture.Bounds.Size);
        var after = picture.TranslatePoint(new Point(0, 0), w)!.Value + new Point(w.Position.X, w.Position.Y);
        Assert.Equal(before, after);
    });

    [Fact]
    public Task Startup_seeding_keeps_remote_and_voice_flashes_point_fired_and_skips_a_full_ambient_tick() => OnUi((s, _) =>
    {
        var (show, ambient, busy, fire) = (CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider, FlashOverlay.Fire);
        var (click, stay) = (s.FlashClickable, s.FlashStayUntilPopped);
        var fired = new System.Collections.Generic.List<bool>();
        var host = new Window();
        try
        {
            FlashOverlay.Fire = (_, isAmbient) => fired.Add(isAmbient);
            FlashOverlay.SeedCore(() => host, () => { });
            CoreFlash.ShowProvider!();          // remote trigger_flash, voice flash_once, autonomy
            CoreFlash.AmbientShowProvider!();   // the scheduler tick
            Assert.Equal(new[] { false, true }, fired);

            (s.FlashClickable, s.FlashStayUntilPopped) = (true, true);
            Assert.False(CoreFlash.IsBusyProvider!());
            for (var i = 0; i < FlashStayRule.MaxOnScreen; i++) Shown(clickable: true);
            Assert.True(CoreFlash.IsBusyProvider!());
        }
        finally
        {
            (CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider, FlashOverlay.Fire) = (show, ambient, busy, fire);
            (s.FlashClickable, s.FlashStayUntilPopped) = (click, stay);
        }
    });

    [Fact]
    public Task Exit_none_cuts_at_once_and_a_flash_without_click_ignores_the_press() => OnUi((s, _) =>
    {
        s.FlashExitStyle = FlashExitStyle.None;
        var cut = Shown(clickable: true);
        var cutClosed = false;
        cut.Closed += (_, _) => cutClosed = true;
        cut.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.True(cutClosed);
        Assert.Null(cut.Exit);

        s.FlashExitStyle = FlashExitStyle.Pop;
        var passive = Shown(clickable: false);
        passive.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.Contains(FlashOverlay.Active, e => e.Window == passive);
        Assert.Null(passive.Exit);
    });

    [Fact]
    public Task Stop_closes_a_flash_that_is_still_leaving() => OnUi((s, _) =>
    {
        s.FlashExitStyle = FlashExitStyle.Melt;
        s.MotionLevel = MotionLevel.Full;
        var w = Shown(clickable: true);
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.NotNull(w.Exit);

        FlashOverlay.CloseAll(final: false);   // the tray's Stop everything / panic path
        Assert.True(closed);
        Assert.Empty(FlashOverlay.Exiting);
    });

    [Fact]
    public Task The_flash_rack_offers_stay_and_the_leave_picker_greyed_with_click() => OnUi((s, _) =>
    {
        var (click, stay) = (s.FlashClickable, s.FlashStayUntilPopped);
        var host = new global::Avalonia.Controls.Window();
        try
        {
            s.FlashClickable = false;
            s.FlashExitStyle = FlashExitStyle.Spiral;
            var card = new global::ConditioningControlPanel.Avalonia.Views.Features.FlashFeatureControl();
            host.Content = card;
            host.Show();
            var chk = card.FindControl<global::Avalonia.Controls.CheckBox>("ChkStayUntilPopped")!;
            var cmb = card.FindControl<global::Avalonia.Controls.ComboBox>("CmbExit")!;
            Assert.Equal(new[] { FlashExitStyle.Mix, FlashExitStyle.Pop, FlashExitStyle.TvOff, FlashExitStyle.Spiral,
                FlashExitStyle.Melt, FlashExitStyle.Glitch, FlashExitStyle.None },
                cmb.Items.OfType<global::Avalonia.Controls.ComboBoxItem>().Select(i => (FlashExitStyle)i.Tag!));
            Assert.Equal(FlashExitStyle.Spiral, ((global::Avalonia.Controls.ComboBoxItem)cmb.SelectedItem!).Tag);
            Assert.False(chk.IsEffectivelyEnabled);
            Assert.False(cmb.IsEffectivelyEnabled);

            card.FindControl<global::Avalonia.Controls.CheckBox>("ChkClickable")!.IsChecked = true;
            Assert.True(chk.IsEffectivelyEnabled);
            chk.IsChecked = true;
            Assert.True(s.FlashStayUntilPopped);
            cmb.SelectedIndex = 6;
            Assert.Equal(FlashExitStyle.None, s.FlashExitStyle);
        }
        finally
        {
            host.Close();
            (s.FlashClickable, s.FlashStayUntilPopped) = (click, stay);
        }
    });

    [Fact]
    public void Stay_until_popped_sets_the_safety_lifetime_and_cap_for_ambient_clickable_flashes_only()
    {
        var s = new AppSettings { FlashDuration = 5, FlashClickable = true, FlashStayUntilPopped = true };
        Assert.Equal(TimeSpan.FromMinutes(10), FlashOverlay.Lifetime(s, ambient: true, durationMs: null));
        Assert.Equal(40, FlashOverlay.Cap(s, ambient: true));
        // Point-fired (test button, companion command) keeps its authored timing.
        Assert.Equal(TimeSpan.FromSeconds(6), FlashOverlay.Lifetime(s, ambient: false, durationMs: null));
        Assert.Equal(TimeSpan.FromSeconds(3), FlashOverlay.Lifetime(s, ambient: false, durationMs: 2000));
        Assert.Equal(s.HydraLimit, FlashOverlay.Cap(s, ambient: false));
        // A flash nobody can click never stays.
        s.FlashClickable = false;
        Assert.Equal(TimeSpan.FromSeconds(6), FlashOverlay.Lifetime(s, ambient: true, durationMs: null));
        Assert.Equal(s.HydraLimit, FlashOverlay.Cap(s, ambient: true));
    }

    [Fact]
    public Task A_full_screen_of_staying_flashes_skips_the_ambient_tick() => OnUi((_, _) =>
    {
        var s = new AppSettings { FlashClickable = true, FlashStayUntilPopped = true };
        var windows = Enumerable.Range(0, FlashStayRule.MaxOnScreen - 1).Select(_ => Shown(clickable: true)).ToList();
        Assert.False(FlashOverlay.StayScreenFull(s));
        windows.Add(Shown(clickable: true));
        Assert.True(FlashOverlay.StayScreenFull(s));
        s.FlashStayUntilPopped = false;
        Assert.False(FlashOverlay.StayScreenFull(s));
    });
}
