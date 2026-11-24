using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Preview;
using ConditioningControlPanel.Controls.Preview.Scenes;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Live Preview pilot (owner 2026-11-24): the side-art card of the Flash page becomes a framed slice of a
/// desktop where a simplified Flash Images plays through the real FlashLayer with fake pictures. These
/// pin the wake rule on an injected clock, the motion gate and the Flash scene's limits. With env
/// CCP_ROWS_DUMP=&lt;dir&gt; the render tests save flash-preview-empty.png and flash-preview.png.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class EffectPreviewTests
{
    private sealed class CountingScene : IPreviewScene
    {
        public int Ticks, Changes, Clicks, Starts;
        public string[] Settings { get; } = { nameof(AppSettings.ImageScale), nameof(AppSettings.FlashFrequency) };
        public void Layout(SKRect screen, SKRect visible) { }
        public void Start() => Starts++;
        public void Tick(double dt) => Ticks++;
        public void Render(SKCanvas canvas, SKRect screen, SKRect visible) { }
        public void SettingChanged(string name) => Changes++;
        public void Click(SKPoint at) => Clicks++;
        public void Dispose() { }
    }

    private static (PreviewDriver Driver, CountingScene Scene, Func<double> Now, Action<double> Advance) Rig()
    {
        double now = 100;
        var scene = new CountingScene();
        var driver = new PreviewDriver(scene, () => now);
        return (driver, scene, () => now, s => now += s);
    }

    /// <summary>Run the host's frame clock for <paramref name="seconds"/> at 30 fps; returns the frames that moved.</summary>
    private static int RunFrames(PreviewDriver d, Action<double> advance, double seconds)
    {
        int moved = 0;
        for (double t = 0; t < seconds; t += 1 / 30.0)
        {
            advance(1 / 30.0);
            if (d.WantsClock && d.Frame()) moved++;
        }
        return moved;
    }

    [Fact]
    public void A_watched_setting_wakes_it_and_an_unwatched_one_does_not()
    {
        var (d, scene, _, advance) = Rig();
        Assert.False(d.WantsClock);
        Assert.False(d.SettingChanged(nameof(AppSettings.SubliminalFrequency)));
        Assert.False(d.WantsClock);
        Assert.Equal(0, scene.Changes);

        Assert.True(d.SettingChanged(nameof(AppSettings.ImageScale)));
        Assert.Equal(1, scene.Changes);
        Assert.True(d.WantsClock);
        Assert.True(RunFrames(d, advance, 1) > 20);
    }

    [Fact]
    public void It_sleeps_after_the_wake_window_and_a_click_wakes_it_again()
    {
        var (d, scene, _, advance) = Rig();
        d.Open();
        Assert.Equal(1, scene.Starts);
        RunFrames(d, advance, PreviewDriver.WakeSeconds + 0.5);
        Assert.False(d.WantsClock);
        int ticks = scene.Ticks;
        Assert.Equal(0, RunFrames(d, advance, 5));
        Assert.Equal(ticks, scene.Ticks);

        d.Click(new SKPoint(10, 10));
        Assert.Equal(1, scene.Clicks);
        Assert.True(d.WantsClock);
        Assert.InRange(RunFrames(d, advance, 12), 280, 310);   // about ten seconds at 30 fps, then still
    }

    [Fact]
    public void It_never_ticks_while_hidden_or_while_the_gate_is_closed()
    {
        var (d, scene, _, advance) = Rig();
        d.CanShow = false;
        d.SettingChanged(nameof(AppSettings.FlashFrequency));
        Assert.Equal(0, RunFrames(d, advance, 3));

        d.CanShow = true;
        d.Pace = PreviewGate.Pace(MotionLevel.Full, panelQuiet: true, allowAmbientLoops: false, tierAllowsMotion: true);
        Assert.Equal(PreviewPace.Hold, d.Pace);
        d.SettingChanged(nameof(AppSettings.FlashFrequency));
        d.Click(new SKPoint(1, 1));
        Assert.Equal(0, RunFrames(d, advance, 3));
        Assert.Equal(0, scene.Ticks);
        Assert.Equal(0, scene.Clicks);   // held: a click sets nothing moving

        // Full but AllowAmbientLoops false (a tier without ambient motion): the still, never the clock.
        d.Pace = PreviewGate.Pace(MotionLevel.Full, panelQuiet: false, allowAmbientLoops: false, tierAllowsMotion: false);
        Assert.Equal(PreviewPace.Still, d.Pace);
        d.SettingChanged(nameof(AppSettings.FlashFrequency));
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, advance, 3));
    }

    [Theory]
    [InlineData(MotionLevel.Full, false, true, true, PreviewPace.Live)]
    [InlineData(MotionLevel.Full, false, false, true, PreviewPace.Still)]
    [InlineData(MotionLevel.Full, true, true, true, PreviewPace.Hold)]
    [InlineData(MotionLevel.Reduced, false, false, true, PreviewPace.Live)]
    [InlineData(MotionLevel.Reduced, false, false, false, PreviewPace.Still)]
    [InlineData(MotionLevel.Reduced, true, false, true, PreviewPace.Hold)]
    [InlineData(MotionLevel.Off, false, true, true, PreviewPace.Still)]
    public void The_motion_gate(MotionLevel level, bool quiet, bool ambient, bool tier, PreviewPace expected)
        => Assert.Equal(expected, PreviewGate.Pace(level, quiet, ambient, tier));

    [Fact]
    public void Off_motion_never_animates_and_a_change_jumps_to_the_settled_still()
    {
        double now = 0;
        var s = Settings();
        using var scene = Flash(s, MotionLevel.Off);
        var d = new PreviewDriver(scene, () => now) { Pace = PreviewGate.Pace(MotionLevel.Off, false, true, true) };
        d.Open();
        Assert.False(d.WantsClock);
        Assert.Equal(0, d.Ticks);
        // The settle ran the still forward: the burst is in, every picture at its full opacity.
        Assert.True(scene.OnScreen > 0);
        Assert.All(scene.Items, i => Assert.Equal(s.FlashOpacity / 100.0, i.Opacity, 3));
        s.ImageScale = 150;
        d.SettingChanged(nameof(AppSettings.ImageScale));
        now += 3;
        Assert.False(d.WantsClock);
        Assert.Equal(0, d.Ticks);
    }

    // ---------------------------------------------------------------- the Flash scene

    private static AppSettings Settings()
    {
        var s = new AppSettings
        {
            FlashFrequency = 180, SimultaneousImages = 4, SimultaneousImagesRandom = false, HydraLimit = 8,
            FlashClickable = true, FlashStayUntilPopped = false, CorruptionMode = true, HydraLinkedTiming = true,
            ImageScale = 100, FlashOpacity = 100, FadeDuration = 30, FlashGlowEnabled = false, FlashAvoidCenter = false,
        };
        return s;
    }

    private static FlashPreviewScene Flash(AppSettings s, MotionLevel level = MotionLevel.Full, bool owns = false, int seed = 7, float height = 1100, SKRect? visible = null)
    {
        var scene = new FlashPreviewScene(() => s, () => level, () => false, _ => owns, new Random(seed));
        var screen = new SKRect(0, 0, 378, height - 22);
        scene.Layout(screen, visible ?? screen);
        return scene;
    }

    private static void Run(IPreviewScene scene, double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) scene.Tick(1 / 30.0);
    }

    private static SKPoint Centre(ConditioningControlPanel.Services.Compositor.FlashLayer.FlashItem i) => new(i.X + i.W / 2, i.Y + i.H / 2);

    [Fact]
    public void Max_on_screen_is_never_exceeded()
    {
        var s = Settings();
        s.SimultaneousImages = 12; s.HydraLimit = 3; s.FlashStayUntilPopped = true;
        using var scene = Flash(s);
        scene.Start();
        for (int i = 0; i < 300; i++)
        {
            scene.Tick(1 / 30.0);
            Assert.True(scene.OnScreen <= 3, $"{scene.OnScreen} on screen at {scene.Clock:0.0}s");
            if (i % 20 == 0 && scene.Items.FirstOrDefault() is { } first) scene.Click(Centre(first));
        }
        s.HydraLimit = 2;
        scene.SettingChanged(nameof(AppSettings.HydraLimit));
        Run(scene, 1);
        Assert.True(scene.OnScreen <= 2);
    }

    [Fact]
    public void Hydra_adds_two_and_a_plain_click_only_pops()
    {
        var s = Settings();
        s.SimultaneousImages = 1; s.FlashStayUntilPopped = true; s.FlashFrequency = 1;
        using var scene = Flash(s);
        scene.Start();
        Run(scene, 0.8);
        Assert.Equal(1, scene.OnScreen);
        scene.Click(Centre(scene.Items.Single()));
        Assert.Equal(2, scene.OnScreen);

        s.CorruptionMode = false;
        Run(scene, 0.5);   // the two new ones fade in before they take a click
        scene.Click(Centre(scene.Items.First()));
        Assert.Equal(1, scene.OnScreen);
    }

    [Fact]
    public void A_click_with_click_off_does_nothing()
    {
        var s = Settings();
        s.SimultaneousImages = 2; s.FlashClickable = false; s.FlashFrequency = 1;
        using var scene = Flash(s);
        scene.Start();
        Run(scene, 0.8);
        int before = scene.OnScreen;
        Assert.True(before > 0);
        foreach (var item in scene.Items.ToList()) scene.Click(Centre(item));
        Assert.Equal(before, scene.OnScreen);
    }

    [Fact]
    public void New_pictures_land_in_the_band_that_is_on_screen()
    {
        var s = Settings();
        s.SimultaneousImages = 6; s.HydraLimit = 20; s.FlashStayUntilPopped = true;
        var band = new SKRect(0, 900, 378, 1300);
        using var scene = Flash(s, height: 1800, visible: band);
        scene.Start();
        Run(scene, 3);
        Assert.True(scene.OnScreen >= 6);
        Assert.All(scene.Items, i => Assert.InRange(i.Y + i.H / 2, band.Top, band.Bottom));

        // With the whole slice on screen they spread over all of it, top half and bottom half.
        using var all = Flash(s, height: 1800, seed: 3);
        all.Start();
        Run(all, 5);
        Assert.Contains(all.Items, i => i.Y + i.H / 2 < 889);
        Assert.Contains(all.Items, i => i.Y + i.H / 2 > 889);

        // A sliver under 160 DIP falls back to the top of the slice, as the mockup does.
        scene.Layout(new SKRect(0, 0, 378, 1778), new SKRect(0, 1700, 378, 1778));
        Assert.Equal(0, scene.SpawnBand.Top);
    }

    [Fact]
    public void Avoid_screen_center_keeps_the_middle_of_the_visible_band_free()
    {
        var s = Settings();
        s.SimultaneousImages = 1; s.HydraLimit = 20; s.FlashStayUntilPopped = true;
        s.FlashAvoidCenter = true; s.FlashCenterExclusionPercent = 40; s.ImageScale = 50;
        var band = new SKRect(0, 400, 378, 1000);
        using var scene = Flash(s, height: 1800, visible: band);
        scene.Start();
        Run(scene, 14);
        Assert.True(scene.OnScreen >= 5);
        Assert.All(scene.Items, i => Assert.False(i.X + i.W / 2 > 160 && i.X + i.W / 2 < 218 && i.Y + i.H / 2 > 671 && i.Y + i.H / 2 < 729,
            $"a picture sits in the keep-out at {i.X + i.W / 2:0},{i.Y + i.H / 2:0}"));
    }

    // ---------------------------------------------------------------- renders

    [Fact]
    [Trait("Category", "Render")]
    public void The_empty_slice_and_a_busy_moment_render_and_dump_when_asked()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            EffectPreview.DesktopScaleOverride = 1.5;
            try
            {
                var empty = new EffectPreview { Width = 400, Height = 1100 };
                Layout(empty);
                var dir = Environment.GetEnvironmentVariable("CCP_ROWS_DUMP");
                var emptyShot = Shot(empty, 144);
                // The frame is dark, the screen inside it is the blue wallpaper, the taskbar sits at the bottom.
                Assert.True(Brightness(emptyShot, 3, 550) < 40);
                var wall = Pixel(emptyShot, 300, 1500);
                Assert.True(wall.B > wall.R && wall.B > 40, $"wallpaper is {wall}");
                if (dir is { Length: > 0 }) { Directory.CreateDirectory(dir); Save(emptyShot, Path.Combine(dir, "flash-preview-empty.png")); }

                var s = Settings();
                s.FlashGlowEnabled = true; s.FlashRoundedCorners = true; s.FlashStayUntilPopped = true;
                s.SimultaneousImages = 3; s.HydraLimit = 9;
                var scene = new FlashPreviewScene(() => s, () => MotionLevel.Full, () => false, _ => true, new Random(11));
                var busy = new EffectPreview { Width = 400, Height = 1100 };
                busy.UseScene(scene);
                busy.RasterScale = 0.75;   // what half resolution gives on a 150% screen
                Layout(busy);
                busy.Driver!.Open();
                Run(scene, 3.4);
                busy.Repaint();
                Assert.True(scene.OnScreen >= 6);
                var shot = Shot(busy, 144);
                if (dir is { Length: > 0 }) Save(shot, Path.Combine(dir, "flash-preview.png"));
                var item = scene.Items.First();
                var at = Pixel(shot, (int)((item.X + item.W / 2 + PreviewDesktop.Bezel) * 1.5), (int)((item.Y + item.H / 2 + PreviewDesktop.Bezel) * 1.5));
                Assert.NotEqual(wall, at);
                scene.Dispose();
            }
            finally { EffectPreview.DesktopScaleOverride = null; }
        });
    }

    private static void Layout(FrameworkElement e)
    {
        e.Measure(new Size(e.Width, e.Height));
        e.Arrange(new Rect(0, 0, e.Width, e.Height));
        e.UpdateLayout();
    }

    private static BitmapSource Shot(FrameworkElement e, double dpi)
    {
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth * dpi / 96), (int)Math.Ceiling(e.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(e);
        bmp.Freeze();
        return bmp;
    }

    private static Color Pixel(BitmapSource b, int x, int y)
    {
        var px = new byte[4];
        b.CopyPixels(new Int32Rect(Math.Clamp(x, 0, b.PixelWidth - 1), Math.Clamp(y, 0, b.PixelHeight - 1), 1, 1), px, 4, 0);
        return Color.FromArgb(px[3], px[2], px[1], px[0]);
    }

    private static double Brightness(BitmapSource b, int x, int y) { var c = Pixel(b, x, y); return (c.R + c.G + c.B) / 3.0; }

    private static void Save(BitmapSource b, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>The preview shows fake pictures only: nothing under Controls/Preview reaches for the user's
    /// media lists, a pack decrypt, Scrolller, audio playback or a settings write.</summary>
    [Fact]
    public void Nothing_under_Controls_Preview_touches_media_packs_scrolller_audio_or_saves()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Controls", "Preview")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Controls", "Preview"), "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length >= 5);
        string[] banned =
        {
            "GetChaosImagePaths", "GetNextImages", "LoadImageAsync", "ImagePaths", "assets/images", @"assets\images",
            "PackEncryption", "DecryptFile", "ContentPack", "ModResourceResolver", "Scrolller",
            "NAudio", "WaveOut", "LibVLC", "MediaPlayer", "SoundPlayer", "AudioService", "App.Audio", "PlaySound", "App.Haptics", "Services.Haptics",
            "Settings?.Save", "Settings.Save", "SaveSettings", "App.Flash",
        };
        var hits = (from f in files
                    let text = File.ReadAllText(f)
                    from word in banned
                    where text.Contains(word, StringComparison.Ordinal)
                    select $"{Path.GetFileName(f)}: {word}").ToList();
        Assert.True(hits.Count == 0, "the preview reaches for real media or effects: " + string.Join(", ", hits));
    }
}
