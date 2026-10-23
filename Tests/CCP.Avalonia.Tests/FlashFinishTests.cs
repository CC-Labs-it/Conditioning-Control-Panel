using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashFinishTests
{
    private sealed class Clock : TimeProvider
    {
        internal long Now = TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(double seconds) => Now += (long)(seconds * TimeSpan.TicksPerSecond);
    }

    private static Task OnUi(Action body) => OnUi(() => { body(); return Task.CompletedTask; });

    private static Task OnUi(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        try { await body(); }
        finally { FlashOverlay.CloseAll(final: false); }
    });

    [Fact]
    public Task A_real_spawn_clips_owned_rounded_corners() => OnUi(() =>
    {
        var s = CoreSettings.Current;
        s.FlashRoundedCorners = true;
        PrizeOwnership.IsGranted = _ => true;
        FlashOverlay.Present = (_, _, _) => true;
        var w = new FlashOverlayWindow();
        var screen = w.Screens.All[0];
        // The production Show route must configure the clip; setting the Core property alone did nothing.
        w.Close();
        using var stream = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png"));
        Assert.True(FlashOverlay.Show(new global::Avalonia.Media.Imaging.Bitmap(stream), new PixelRect(0, 0, 400, 300),
            "sample.png", screen, 1, TimeSpan.Zero, TimeSpan.FromSeconds(6), true, false, 0));
        var shown = FlashOverlay.Active.Single().Window;
        shown.UpdateLayout();
        var image = ((Panel)((Panel)shown.Content!).Children[0]).Children.OfType<Image>().Single();
        var clip = Assert.IsType<RectangleGeometry>(image.Clip);
        Assert.True(clip.RadiusX > 0);
        Assert.Equal(clip.RadiusX, clip.RadiusY);
    });

    [Fact]
    public Task Glitch_builds_coloured_ghosts_alongside_the_seven_slices() => OnUi(() =>
    {
        var w = new FlashOverlayWindow();
        FlashOverlay.Active.Add((w, new PixelRect(0, 0, 400, 300)));
        w.Closed += (_, _) => FlashOverlay.Active.RemoveAll(e => e.Window == w);
        w.Show();
        w.UpdateLayout();
        w.BeginExit(FlashExit.Begin(FlashExitStyle.Glitch, MotionLevel.Full, 3));
        Assert.Equal(7, w.Slices!.Children.Count);
        Assert.Contains(w.GetVisualDescendants(), v => (v as Control)?.Name == "FlashGlitchGhosts");
    });
    [Theory]
    [InlineData(1, false, true, 1)]
    [InlineData(8, false, false, 8)]
    [InlineData(50, false, true, 20)]
    [InlineData(1, true, true, 40)]
    [InlineData(8, true, false, 8)]
    public void Ordinary_flashes_obey_max_on_screen_but_ambient_stay_keeps_forty(int limit, bool stay, bool ambient, int cap)
    {
        var s = new AppSettings { HydraLimit = limit, FlashStayUntilPopped = stay, FlashClickable = true };
        Assert.Equal(cap, FlashOverlay.Cap(s, ambient));
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, false, 0)]
    [InlineData(true, true, false, 1)]
    [InlineData(false, false, true, 2)]
    public Task Corners_honour_the_switch_ownership_and_glow_baseline(bool rounded, bool owns, bool glow, int expected) => OnUi(() =>
    {
        var w = new FlashOverlayWindow();
        if (glow) w.ApplyGlow(new FlashGlowLook(FlashFxRules.Gold, 24, 0.9, false, false), 1);
        w.ConfigureCorners(rounded, owns);
        w.Show();
        w.UpdateLayout();
        try
        {
            var card = (Panel)((Panel)w.Content!).Children[0];
            var image = card.Children.OfType<Image>().Single();
            if (expected == 0) Assert.Null(image.Clip);
            else
            {
                var clip = Assert.IsType<RectangleGeometry>(image.Clip);
                Assert.Equal(expected == 1 ? Math.Min(clip.Rect.Width, clip.Rect.Height) * 0.07 : 12, clip.RadiusX, 6);
                Assert.Equal(new CornerRadius(clip.RadiusX), card.Children.OfType<Border>().First().CornerRadius);
            }
        }
        finally { w.Close(); }
    });

    [Fact]
    public Task Rounded_corner_switch_is_owned_rebinds_and_saves() => OnUi(() =>
    {
        PrizeOwnership.IsGranted = _ => true;
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")));
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.FlashRoundedCorners = false;
        var card = new FlashFeatureControl();
        var host = new Window { Content = card };
        host.Show();
        try
        {
            Assert.True(card.FindControl<Grid>("RowRoundedCorners")!.IsVisible);
            card.FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked = true;
            Assert.True(CoreSettings.Current.FlashRoundedCorners);
            CoreSettings.SaveImmediate();
            Assert.True(new SettingsService().Current.FlashRoundedCorners);
            Assert.True(new FlashFeatureControl().FindControl<CheckBox>("ChkFlashRoundedCorners")!.IsChecked);
            PrizeOwnership.IsGranted = _ => false;
            Assert.False(new FlashFeatureControl().FindControl<Grid>("RowRoundedCorners")!.IsVisible);
        }
        finally { host.Close(); }
    });

    [Fact]
    public Task Lucky_pulse_follows_elapsed_time_through_the_leave_and_stops_on_close() => OnUi(() =>
    {
        var clock = new Clock();
        FlashOverlay.Clock = clock;
        var w = new FlashOverlayWindow { SpawnedAt = clock.Now };
        w.ApplyGlow(new FlashGlowLook(FlashFxRules.Gold, 24, 0.9, true, false), 1);
        w.Show();
        w.UpdateLayout();
        try
        {
            w.PulseFrame();
            Assert.Equal(24, w.GlowShadow[0].Blur);
            Assert.Equal(178, w.GlowShadow[0].Color.A);
            clock.Advance(0.4);
            w.PulseFrame();
            Assert.Equal(38.4, w.GlowShadow[0].Blur, 6);
            Assert.Equal(255, w.GlowShadow[0].Color.A);
            w.BeginExit(FlashExit.Begin(FlashExitStyle.Pop, MotionLevel.Full, 2));
            clock.Advance(0.4);
            w.PulseFrame();
            Assert.Equal(24, w.GlowShadow[0].Blur, 6);
            Assert.Equal(178, w.GlowShadow[0].Color.A);
        }
        finally { w.Close(); }
        var shadow = w.GlowShadow;
        clock.Advance(0.4);
        w.PulseFrame();
        Assert.Equal(shadow, w.GlowShadow);
    });

    [Fact]
    public Task Glitch_ghost_copies_the_bitmap_pixels_in_their_own_channel_order() => OnUi(() =>
    {
        using var bmp = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
            global::Avalonia.Platform.PixelFormats.Rgba8888, global::Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = bmp.Lock())
            for (var i = 0; i < 4; i++)
                System.Runtime.InteropServices.Marshal.WriteInt32(fb.Address, i * 4, unchecked((int)0xFF0000FF)); // R=FF,G=0,B=0,A=FF
        using var ghosts = new FlashGlitchGhosts(bmp);
        using var pixels = SKBitmap.FromImage(ghosts.Picture);
        Assert.Equal(new SKColor(255, 0, 0, 255), pixels.GetPixel(1, 1));
    });

    [Fact]
    public void Glitch_ghost_pixels_are_red_on_the_left_cyan_on_the_right_and_absent_at_zero()
    {
        using var source = SKSurface.Create(new SKImageInfo(100, 60));
        source.Canvas.Clear(SKColors.White);
        using var image = source.Snapshot();
        using var target = SKSurface.Create(new SKImageInfo(120, 60));
        target.Canvas.Clear(SKColors.Transparent);
        var fit = new SKRect(10, 0, 110, 60);
        FlashGlitchGhosts.Draw(target.Canvas, image, fit, 0, 1);
        using (var emptyImage = target.Snapshot())
        using (var empty = SKBitmap.FromImage(emptyImage)) Assert.Equal(0, empty.GetPixel(8, 20).Alpha);
        FlashGlitchGhosts.Draw(target.Canvas, image, fit, 1, 1);
        using var snapshot = target.Snapshot();
        using var pixels = SKBitmap.FromImage(snapshot);
        var red = pixels.GetPixel(8, 20);
        var cyan = pixels.GetPixel(112, 20);
        Assert.True(red.Alpha > 0 && red.Red > red.Blue && red.Red > red.Green);
        Assert.True(cyan.Alpha > 0 && cyan.Blue > cyan.Red && cyan.Green > cyan.Red);
    }

    [Fact]
    public Task Lucky_toast_refreshes_during_fade_reuses_after_hide_and_closes_for_panic() => OnUi(() =>
    {
        var clock = new Clock();
        FlashOverlay.Clock = clock;
        LuckyFlashToast.ScreenOwned = () => false;
        PixelRect placed = default;
        LuckyFlashToast.Prepare = (_, rect) => { placed = rect; return true; };
        FlashOverlay.LuckyChime = _ => { };
        CoreSettings.Current.SuppressPerkNotifications = false;
        FlashOverlay.Paid("sample.png", CoreSettings.Current, 0, 10);
        var toast = Assert.IsType<LuckyFlashToast>(LuckyFlashToast.Active);
        Assert.True(toast.IsVisible);
        var screen = toast.Screens.Primary ?? toast.Screens.All[0];
        Assert.Equal(screen.WorkingArea.Y + (int)(40 * screen.Scaling), placed.Y);
        Assert.InRange(Math.Abs(placed.X + placed.Width / 2 - (screen.WorkingArea.X + screen.WorkingArea.Width / 2)), 0, 1);
        clock.Advance(0.1);
        toast.Tick();
        Assert.Equal(0.5, toast.Alpha, 6);
        clock.Advance(3.05);
        toast.Tick();
        Assert.InRange(toast.Alpha, 0.49, 0.51);
        LuckyFlashToast.ShowLucky(10);
        Assert.Same(toast, LuckyFlashToast.Active);
        Assert.Equal(1, toast.Alpha);
        clock.Advance(3.31);
        toast.Tick();
        Assert.False(toast.IsVisible);
        LuckyFlashToast.ShowLucky(10);
        Assert.Same(toast, LuckyFlashToast.Active);
        Assert.True(toast.IsVisible);
        PanicSurfaces.All.Single(s => s.Id == "lucky-flash-toast").Stop(null);
        Assert.Null(LuckyFlashToast.Active);
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task Lucky_toast_respects_notice_opt_out_and_screen_ownership(bool suppressed, bool game) => OnUi(() =>
    {
        CoreSettings.Current.SuppressPerkNotifications = suppressed;
        LuckyFlashToast.ScreenOwned = () => game;
        LuckyFlashToast.Prepare = (_, _) => throw new InvalidOperationException("Skipped toasts must not create a native surface");
        LuckyFlashToast.ShowLucky(10);
        Assert.Null(LuckyFlashToast.Active);
    });

    [Fact]
    public Task Hydra_loads_real_pictures_and_CloseAll_drops_the_second_queued_child() => OnUi(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-flash-hydra-").FullName;
        var parent = new FlashOverlayWindow();
        parent.SpawnScreen = parent.Screens.All[0];
        var seen = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CorePaths.EffectiveAssetsProvider = () => dir;
        CoreSettings.Current.FlashGlowEnabled = false;
        CoreSettings.Current.UnlockedSkills.Clear();
        FlashOverlay.DiskBag.Reset();
        FlashOverlay.Present = (_, _, _) => { seen++; first.TrySetResult(); return true; };
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "images"));
            using (var input = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")))
            using (var output = File.Create(Path.Combine(dir, "images", "sample.png"))) input.CopyTo(output);
            await FlashOverlay.SpawnChildren(parent, 2, TimeSpan.FromSeconds(6), 1).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(2, seen);
            Assert.Equal(2, FlashOverlay.Active.Count);
            Assert.All(FlashOverlay.Active, f =>
            {
                Assert.Same(parent.SpawnScreen, f.Window.SpawnScreen);
                Assert.Equal(1, f.Window.HydraGeneration);
                Assert.Equal(TimeSpan.FromSeconds(6), f.Window.Lifetime);
                Assert.Equal(FlashMotionStyle.Still, f.Window.MotionStyle);
            });
            FlashOverlay.CloseAll(final: false);
            seen = 0;
            first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var spawning = FlashOverlay.SpawnChildren(parent, 2, TimeSpan.FromSeconds(6), 1);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            FlashOverlay.CloseAll(final: false);
            await spawning.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, seen);
            Assert.Empty(FlashOverlay.Active);
        }
        finally { FlashOverlay.CloseAll(final: false); parent.Close(); Directory.Delete(dir, recursive: true); }
    });

}
