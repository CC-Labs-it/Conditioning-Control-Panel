using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>What a flash pays and plays beyond the pop (WPF FlashService SpawnFlashWindow /
/// OnFlashClicked / FlashLayer): XP, the achievement + quest tick, the lucky chime, the glow card,
/// the hydra multiply on a pop, and the TvOff whiten, Glitch slices and Pop sparks of a leave.</summary>
public sealed class FlashWireTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static readonly System.Reflection.PropertyInfo AchievementsProp = typeof(AvApp).GetProperty("Achievements",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;

    /// <summary>Settings, clock, seams and App.Achievements restored after every test (P02).</summary>
    private static Task OnUi(Action<AppSettings, SteppedClock, List<(double Xp, string Source)>, AchievementEngine> body) =>
        OnUiAsync((s, clock, xp, engine) => { body(s, clock, xp, engine); return Task.CompletedTask; });

    private static Task OnUiAsync(Func<AppSettings, SteppedClock, List<(double Xp, string Source)>, AchievementEngine, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().WithInterFont()
                    .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                    .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.FlashExitStyle, s.MotionLevel, s.HydraLinkedTiming, s.CorruptionMode, s.HydraLimit,
                s.FlashGlowEnabled, s.PerformanceMode, s.FlashClickable, s.FlashStayUntilPopped, Skills: s.UnlockedSkills.ToList());
            var (clockWas, present, multiply, chime, xpWas, achWas) = (FlashOverlay.Clock, FlashOverlay.Present,
                FlashOverlay.Multiply, FlashOverlay.LuckyChime, CoreProgression.AddXPProvider, AchievementsProp.GetValue(null));
            var dir = Directory.CreateTempSubdirectory("ccp-flash-wire-").FullName;
            var clock = new SteppedClock();
            var xp = new List<(double, string)>();
            try
            {
                FlashOverlay.Clock = clock;
                FlashOverlay.Present = (_, _, _) => true;
                CoreProgression.AddXPProvider = (a, src) => xp.Add((a, src));
                var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
                AchievementsProp.SetValue(null, engine);
                (s.MotionLevel, s.PerformanceMode, s.FlashStayUntilPopped) = (MotionLevel.Full, false, false);
                s.UnlockedSkills.Clear();
                await body(s, clock, xp, engine);
            }
            finally
            {
                FlashOverlay.CloseAll(final: false);
                (FlashOverlay.Clock, FlashOverlay.Present, FlashOverlay.Multiply, FlashOverlay.LuckyChime, CoreProgression.AddXPProvider) =
                    (clockWas, present, multiply, chime, xpWas);
                AchievementsProp.SetValue(null, achWas);
                (s.FlashExitStyle, s.MotionLevel, s.HydraLinkedTiming, s.CorruptionMode, s.HydraLimit,
                    s.FlashGlowEnabled, s.PerformanceMode, s.FlashClickable, s.FlashStayUntilPopped) =
                    (saved.FlashExitStyle, saved.MotionLevel, saved.HydraLinkedTiming, saved.CorruptionMode, saved.HydraLimit,
                        saved.FlashGlowEnabled, saved.PerformanceMode, saved.FlashClickable, saved.FlashStayUntilPopped);
                s.UnlockedSkills.Clear();
                s.UnlockedSkills.AddRange(saved.Skills);
                Directory.Delete(dir, recursive: true);
            }
        });

    private static Bitmap Picture() => new(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));

    /// <summary>One flash through the real show path (lucky roll, glow, Spawn, pay), clickable.</summary>
    private static FlashOverlayWindow ShowOne(int hydraGeneration = 0, bool ambient = false, TimeSpan? lifetime = null)
    {
        var screen = new Window().Screens.All[0];
        Assert.True(FlashOverlay.Show(Picture(), new PixelRect(100, 100, 400, 300), "a.png", screen, 1, TimeSpan.Zero,
            lifetime ?? TimeSpan.FromSeconds(6), clickable: true, ambient, hydraGeneration));
        return FlashOverlay.Active[^1].Window;
    }

    [Fact]
    public Task A_shown_flash_pays_4_xp_and_ticks_the_flash_achievement_counter() => OnUi((s, _, xp, engine) =>
    {
        ShowOne();
        Assert.Equal(new[] { (4.0, "Flash") }, xp);   // no flash sound on this head: WPF's no-sound 4
        Assert.Equal(1, engine.Progress.TotalFlashImages);
    });

    [Fact]
    public Task An_independent_hydra_grandchild_pays_1_xp_and_is_not_counted() => OnUi((s, _, xp, engine) =>
    {
        s.HydraLinkedTiming = false;
        ShowOne(hydraGeneration: 2);
        Assert.Equal(new[] { (1.0, "Flash") }, xp);
        Assert.Equal(0, engine.Progress.TotalFlashImages);
    });

    [Fact]
    public Task A_lucky_flash_chimes_and_pays_ten_times() => OnUi((s, _, xp, _) =>
    {
        var chimes = 0;
        FlashOverlay.LuckyChime = _ => chimes++;
        var quiet = s.SuppressPerkNotifications;
        try
        {
            s.SuppressPerkNotifications = false;
            FlashOverlay.Paid("a.png", s, 0, FlashFxRules.LuckyMultiplier);
            Assert.Equal(1, chimes);
            Assert.Equal(new[] { (40.0, "Flash") }, xp);

            s.SuppressPerkNotifications = true;   // WPF PlayLuckyFlashSound returns early; the XP still pays
            FlashOverlay.Paid("a.png", s, 0, FlashFxRules.LuckyMultiplier);
            Assert.Equal(1, chimes);
            Assert.Equal(2, xp.Count);
        }
        finally { s.SuppressPerkNotifications = quiet; }
    });

    [Fact]
    public void A_hydra_child_takes_its_parents_motion_not_the_setting()
    {
        var seam = PrizeOwnership.IsGranted;
        try
        {
            PrizeOwnership.IsGranted = _ => true;
            var (rect, screen) = (new PixelRect(100, 100, 400, 300), new PixelRect(0, 0, 1920, 1080));
            var s = new AppSettings { FlashMotionStyle = FlashMotionStyle.DriftBounce, FlashDriftSpeed = 1, MotionLevel = MotionLevel.Full };
            Assert.Null(FlashOverlay.BuildMotion(rect, screen, s, new Random(3), inherit: FlashMotionStyle.Still));
            s.FlashMotionStyle = FlashMotionStyle.Still;
            Assert.Equal(FlashMotionStyle.DriftBounce, FlashOverlay.BuildMotion(rect, screen, s, new Random(3), inherit: FlashMotionStyle.DriftBounce)!.Style);
        }
        finally { PrizeOwnership.IsGranted = seam; }
    }

    [Fact]
    public Task Sparkle_boost_wraps_the_flash_in_a_pink_glow_card_and_grows_the_window_round_it() => OnUi((s, _, _, _) =>
    {
        PixelRect presented = default;
        FlashOverlay.Present = (_, r, _) => { presented = r; return true; };
        s.FlashGlowEnabled = true;
        s.UnlockedSkills.Add("sparkle_boost_1");
        var w = ShowOne();
        Assert.Equal(1, w.GlowShadow.Count);
        var glow = w.GlowShadow[0];
        Assert.Equal(24, glow.Blur);   // tier 1: hot pink 25 @ 0.5, capped at 24 (WPF MaxGlowBlurRadius)
        Assert.Equal(Color.FromArgb(127, 0xFF, 0x69, 0xB4), glow.Color);
        Assert.True(w.GlowPadPx > 0);
        Assert.Equal(new PixelRect(100 - w.GlowPadPx, 100 - w.GlowPadPx, 400 + 2 * w.GlowPadPx, 300 + 2 * w.GlowPadPx), presented);

        s.FlashGlowEnabled = false;   // the global glow toggle
        Assert.Equal(0, ShowOne().GlowShadow.Count);
    });

    [Fact]
    public Task A_pop_in_corruption_mode_spawns_two_hydra_children_with_the_parents_remaining_time() => OnUi((s, clock, _, _) =>
    {
        var spawned = new List<(int Count, TimeSpan Life, int Gen)>();
        FlashOverlay.Multiply = (_, n, life, gen) => spawned.Add((n, life, gen));
        (s.FlashExitStyle, s.CorruptionMode, s.HydraLimit, s.HydraLinkedTiming) = (FlashExitStyle.None, true, 10, true);
        var w = ShowOne(lifetime: TimeSpan.FromSeconds(6));
        clock.Now += 2 * TimeSpan.TicksPerSecond;
        w.MouseDown(new Point(50, 50), MouseButton.Left);
        Assert.Equal(new[] { (2, TimeSpan.FromSeconds(4), 1) }, spawned);

        s.CorruptionMode = false;
        ShowOne().MouseDown(new Point(50, 50), MouseButton.Left);
        Assert.Single(spawned);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Hydra_loads_real_disk_images_and_CloseAll_drops_queued_spawns(bool stopBeforeSpawn) => OnUiAsync(async (s, _, _, _) =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-hydra-");
        var previous = CorePaths.EffectiveAssetsProvider;
        try
        {
            var images = Directory.CreateDirectory(Path.Combine(root.FullName, "images"));
            using (var picture = Picture()) picture.Save(Path.Combine(images.FullName, "sample.png"));
            CorePaths.EffectiveAssetsProvider = () => root.FullName;
            s.HydraLimit = 10;
            var parent = ShowOne();
            await FlashOverlay.SpawnChildren(parent, 2, TimeSpan.FromSeconds(6), 1);
            if (stopBeforeSpawn) FlashOverlay.CloseAll(final: false);
            // Let the real 0/100 ms dispatcher timers fire, including refused queued spawns.
            await Task.Delay(200);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (stopBeforeSpawn) Assert.Empty(FlashOverlay.Active);
            else
            {
                var children = FlashOverlay.Active.Where(a => a.Window.HydraGeneration == 1).ToArray();
                Assert.Equal(2, children.Length);
                Assert.All(children, child => Assert.Same(parent.SpawnScreen, child.Window.SpawnScreen));
                Assert.All(children, child => Assert.Equal(TimeSpan.FromSeconds(6), child.Window.Lifetime));
            }
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            CorePaths.EffectiveAssetsProvider = previous;
            root.Delete(recursive: true);
        }
    });

    [Fact]
    public Task A_lucky_bubble_routes_its_mod_label_and_20x_multiplier_to_the_same_toast() => OnUi((_, _, _, _) =>
    {
        var previous = FlashOverlay.LuckyProc;
        try
        {
            var calls = new List<(string, int)>();
            FlashOverlay.LuckyProc = (label, multiplier) => calls.Add((label, multiplier));
            BubbleOverlay.Field.LuckyProc!(20);
            Assert.Equal(new[] { (AvApp.Mods?.GetLuckyBubbleLabel()
                ?? ConditioningControlPanel.Localization.Loc.Get("skill_lucky_bubble"), 20) }, calls);
        }
        finally { FlashOverlay.LuckyProc = previous; }
    });

    [Fact]
    public Task No_glow_keeps_a_black_backing_even_behind_transparent_image_pixels() => OnUi((s, _, _, _) =>
    {
        s.FlashGlowEnabled = false;
        var w = ShowOne();
        Assert.Contains(w.GetVisualDescendants().OfType<Border>(), b =>
            b.Background is ISolidColorBrush brush && brush.Color == Colors.Black);
    });

    [Fact]
    public Task Glitch_draws_opposed_tinted_ghosts_beneath_its_seven_slices() => OnUi((s, clock, _, _) =>
    {
        s.FlashExitStyle = FlashExitStyle.Glitch;
        var w = ShowOne();
        w.Width = 400; w.Height = 300;
        w.CaptureRenderedFrame();
        w.UpdateLayout();
        w.MouseDown(new Point(50, 50), MouseButton.Left);
        w.CaptureRenderedFrame();
        w.UpdateLayout();
        clock.Now += TimeSpan.TicksPerSecond / 10;
        w.ExitFrame();
        var images = w.GetVisualDescendants().OfType<Image>().ToArray();
        var red = Assert.Single(images, i => i.Name == "FlashGlitchRed");
        var cyan = Assert.Single(images, i => i.Name == "FlashGlitchCyan");
        var split = FlashExit.Sample(w.Exit!).Glitch * 0.04 * red.Bounds.Width;
        Assert.True(split > 0.5);
        Assert.Equal(-split, ((TranslateTransform)red.RenderTransform!).X, 6);
        Assert.Equal(split, ((TranslateTransform)cyan.RenderTransform!).X, 6);
        Assert.InRange(red.Opacity, 0.001, 0.5);
        Assert.NotSame(red.Source, cyan.Source);
        Assert.Equal(7, w.Slices!.Children.Count);
        var redFrame = (Bitmap)red.Source!;
        using var encoded = new MemoryStream();
        redFrame.Save(encoded);
        using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
        var pixel = pixels.GetPixel(pixels.Width / 2, pixels.Height / 2);
        Assert.True(pixel.Red > pixel.Green); // modulation, not two untinted copies
    });

    [Fact]
    public Task Tv_off_whitens_glitch_slices_and_pop_sprays_sparks() => OnUi((s, clock, _, _) =>
    {
        FlashOverlayWindow Leave(FlashExitStyle style)
        {
            s.FlashExitStyle = style;
            var w = ShowOne();
            w.UpdateLayout();
            w.MouseDown(new Point(50, 50), MouseButton.Left);
            w.UpdateLayout();
            clock.Now += TimeSpan.TicksPerSecond / 10;
            w.ExitFrame();
            return w;
        }

        var tv = Leave(FlashExitStyle.TvOff);
        Assert.True(tv.WhitenOpacity > 0);
        Assert.Equal(FlashExit.Sample(tv.Exit!).Whiten, tv.WhitenOpacity, 6);

        var glitch = Leave(FlashExitStyle.Glitch);
        Assert.Equal(7, glitch.Slices!.Children.Count);
        Assert.Contains(glitch.Slices.Children, c => ((TranslateTransform)c.RenderTransform!).X != 0);

        var pop = Leave(FlashExitStyle.Pop);
        Assert.Equal(FlashExit.SparkCount(pop.Exit!), pop.Sparks!.Children.Count);
        Assert.Contains(pop.Sparks.Children, c => c.IsVisible && Canvas.GetLeft(c) > 0);
    });
}
