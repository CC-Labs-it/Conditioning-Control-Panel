using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services.Flash;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashShatterWiringTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static Task OnUi(Action<AppSettings, Clock> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.FlashShatterEnabled, s.FlashExitStyle, s.MotionLevel, s.CorruptionMode, s.FlashClickable);
        var oldClock = FlashOverlay.Clock;
        var oldOwns = PrizeOwnership.IsGranted;
        var clock = new Clock();
        FlashOverlay.Clock = clock;
        PrizeOwnership.IsGranted = _ => true;
        (s.FlashShatterEnabled, s.FlashExitStyle, s.MotionLevel, s.CorruptionMode, s.FlashClickable) =
            (true, FlashExitStyle.Spiral, MotionLevel.Reduced, false, true);
        try { body(s, clock); }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            (s.FlashShatterEnabled, s.FlashExitStyle, s.MotionLevel, s.CorruptionMode, s.FlashClickable) = saved;
            FlashOverlay.Clock = oldClock;
            PrizeOwnership.IsGranted = oldOwns;
        }
        return Task.CompletedTask;
    });

    private static FlashOverlayWindow Shown()
    {
        var w = new FlashOverlayWindow { Position = new PixelPoint(400, 300) };
        w.MakeClickable(() => FlashOverlay.Pop(w));
        FlashOverlay.Active.Add((w, new PixelRect(400, 300, 400, 300)));
        w.Show();
        w.UpdateLayout();
        return w;
    }

    [Fact]
    public Task Owned_shatter_wins_the_picker_and_shards_fall_then_close() => OnUi((_, clock) =>
    {
        var w = Shown();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.Null(w.Exit);
        Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == w);
        Assert.Contains(w, FlashOverlay.Exiting);
        Assert.False(w.IsHitTestVisible);
        w.UpdateLayout();
        var shards = w.GetVisualDescendants().OfType<Control>().Where(i => i.Name == "FlashShard").ToArray();
        Assert.Equal(4, shards.Length);
        Assert.All(shards, i => Assert.IsType<RectangleGeometry>(i.Clip));
        clock.Now += TimeSpan.TicksPerSecond / 10;
        w.ExitFrame();
        Assert.All(shards, i => Assert.True(((TransformGroup)i.RenderTransform!).Children.OfType<TranslateTransform>().Single().Y > 0));
        Assert.False(closed);
        clock.Now += TimeSpan.TicksPerSecond;
        w.ExitFrame();
        Assert.True(closed);
        Assert.Empty(FlashOverlay.Exiting);
    });

    [Theory]
    [InlineData(false, true, MotionLevel.Full)]
    [InlineData(true, false, MotionLevel.Full)]
    [InlineData(true, true, MotionLevel.Off)]
    public Task Unowned_disabled_or_motion_off_uses_the_normal_exit(bool enabled, bool owned, MotionLevel motion) => OnUi((s, _) =>
    {
        s.FlashShatterEnabled = enabled;
        s.MotionLevel = motion;
        PrizeOwnership.IsGranted = _ => owned;
        var w = Shown();
        w.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.NotNull(w.Exit);
        Assert.Empty(w.GetVisualDescendants().OfType<Control>().Where(i => i.Name == "FlashShard"));
    });

    [Fact]
    public Task A_shatter_freezes_drift_until_the_last_break_closes() => OnUi((_, clock) =>
    {
        var w = Shown();
        var motion = FlashMotion.Create(FlashMotionStyle.DriftBounce, 400, 300, 400, 300,
            0, 0, 1920, 1080, MotionLevel.Full, new Random(41));
        FlashOverlay.StartDrift(w, motion);
        w.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.NotNull(w.Shatter);
        var position = w.Position;
        clock.Now += TimeSpan.TicksPerSecond / 10;
        FlashOverlay.DriftTick();
        Assert.Equal(position, w.Position);
        FlashOverlay.CloseAll(final: false);
        Assert.False(FlashOverlay.DriftRunning);
    });

    [Fact]
    public Task Panic_closes_a_shatter_mid_break() => OnUi((_, _) =>
    {
        var w = Shown();
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.MouseDown(new Point(200, 150), MouseButton.Left);
        Assert.Contains(w, FlashOverlay.Exiting);
        ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(p => p.Id == "flash").Stop(null);
        Assert.True(closed);
        Assert.Empty(FlashOverlay.Exiting);
    });

    [Fact]
    public Task Owned_shatter_row_round_trips_and_explains_the_disabled_exit_picker() => OnUi((s, _) =>
    {
        var card = new FlashFeatureControl();
        var host = new Window { Content = card };
        host.Show();
        try
        {
            var toggle = card.FindControl<CheckBox>("ChkFlashShatter");
            Assert.NotNull(toggle);
            var picker = card.FindControl<ComboBox>("CmbExit")!;
            var note = card.FindControl<TextBlock>("TxtExitShatterNote")!;
            Assert.True(toggle!.IsChecked);
            Assert.True(note.IsVisible);
            Assert.False(picker.IsEnabled);
            toggle.IsChecked = false;
            Assert.False(s.FlashShatterEnabled);
            Assert.False(note.IsVisible);
            Assert.True(picker.IsEnabled);
        }
        finally { host.Close(); }
    });

    [Theory]
    [InlineData("", false)]
    [InlineData(PrizeOwnership.FlashPendulum, true)]
    [InlineData(PrizeOwnership.FlashDriftBounce, true)]
    public Task Shatter_row_and_v2_box_show_only_with_a_v2_prize(string owned, bool shown) => OnUi((_, _) =>
    {
        PrizeOwnership.IsGranted = id => id == owned;
        var card = new FlashFeatureControl();
        var host = new Window { Content = card };
        host.Show();
        try
        {
            Assert.Equal(shown, card.FindControl<Control>("RowShatter")!.IsVisible);
            Assert.Equal(shown, card.FindControl<Control>("BoxFlashV2")!.IsVisible);
        }
        finally { host.Close(); }
    });
}
