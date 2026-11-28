using System;
using System.IO;
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
/// Live Preview lane P3 (2026-11-24): the Spiral, Pink Filter, Brain Drain and Mind Wipe scenes. Each one
/// on an injected clock: a watched setting wakes it, it sleeps after the wake window, clicks do what the
/// real effect would, Motion Off never animates. With env CCP_ROWS_DUMP=&lt;dir&gt; the render test saves
/// spiral-preview.png, pinkfilter-preview.png, braindrain-preview.png and mindwipe-preview.png (400 x 1100).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PreviewP3Tests
{
    private static readonly SKRect Screen = new(0, 0, 378, 1078);

    private static IPreviewScene Make(string id, AppSettings s, MotionLevel level = MotionLevel.Full, bool lit = false) => id switch
    {
        "spiral" => new SpiralPreviewScene(() => s, () => level, () => false, () => lit),
        "pinkfilter" => new PinkFilterPreviewScene(() => s, () => level, () => false, () => lit, () => (255, 105, 180)),
        "braindrain" => new BrainDrainPreviewScene(() => s, () => level, () => false, () => lit),
        _ => new MindWipePreviewScene(() => s, () => level, () => false),
    };

    private static T Laid<T>(T scene, SKRect? visible = null) where T : IPreviewScene
    {
        scene.Layout(Screen, visible ?? Screen);
        return scene;
    }

    private static void Run(IPreviewScene scene, double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) scene.Tick(1 / 30.0);
    }

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

    [Theory]
    [InlineData("spiral")]
    [InlineData("pinkfilter")]
    [InlineData("braindrain")]
    [InlineData("mindwipe")]
    public void A_watched_setting_wakes_it_and_it_sleeps_after_the_wake_window(string id)
    {
        double now = 50;
        using var scene = Laid(Make(id, new AppSettings()));
        var d = new PreviewDriver(scene, () => now);
        Assert.False(d.WantsClock);
        Assert.False(d.SettingChanged(nameof(AppSettings.SubliminalFrequency)));
        Assert.False(d.WantsClock);

        Assert.True(d.SettingChanged(scene.Settings[0]));
        Assert.True(d.WantsClock);
        Assert.InRange(RunFrames(d, s => now += s, PreviewDriver.WakeSeconds + 1), 280, 310);
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 4));

        Assert.True(d.Click(new SKPoint(100, 300)));
        Assert.True(d.WantsClock);
    }

    [Theory]
    [InlineData("spiral")]
    [InlineData("pinkfilter")]
    [InlineData("braindrain")]
    [InlineData("mindwipe")]
    public void Off_motion_never_animates_and_held_never_moves(string id)
    {
        double now = 0;
        using var scene = Laid(Make(id, new AppSettings(), MotionLevel.Off, lit: true));
        var d = new PreviewDriver(scene, () => now) { Pace = PreviewGate.Pace(MotionLevel.Off, false, true, true) };
        d.Open();
        d.SettingChanged(scene.Settings[0]);
        d.Click(new SKPoint(120, 300));
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
        Assert.Equal(0, d.Ticks);

        d.Pace = PreviewGate.Pace(MotionLevel.Full, panelQuiet: true, allowAmbientLoops: false, tierAllowsMotion: true);
        Assert.False(d.Click(new SKPoint(1, 1)));
        d.SettingChanged(scene.Settings[0]);
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
    }

    [Fact]
    public void Every_scene_id_resolves_by_its_class_name()
    {
        foreach (var id in new[] { "spiral", "pinkfilter", "braindrain", "mindwipe" })
        {
            var type = typeof(FlashPreviewScene).Assembly.GetType(typeof(FlashPreviewScene).Namespace + "." + id + "PreviewScene", false, true);
            Assert.NotNull(type);
            using var scene = (IPreviewScene)Activator.CreateInstance(type!, nonPublic: true)!;
            Assert.NotEmpty(scene.Settings);
        }
    }

    // ---------------------------------------------------------------- spiral

    [Fact]
    public void Spiral_follows_its_opacity_turns_only_with_motion_and_holds_still_off()
    {
        var s = new AppSettings { SpiralOpacity = 20 };
        using var scene = Laid(new SpiralPreviewScene(() => s, () => MotionLevel.Full, () => false, () => false));
        Run(scene, 1.5);
        double low = scene.DrawOpacity;
        s.SpiralOpacity = 90;
        Assert.True(scene.DrawOpacity > low * 2);
        Assert.True(scene.Angle > 30);
        Assert.Equal(0, SpiralPreviewScene.PreviewOpacity(0));
        Assert.False(scene.PoolOn);

        using var off = Laid(new SpiralPreviewScene(() => s, () => MotionLevel.Off, () => false, () => false));
        Run(off, 1.5);
        Assert.Equal(0, off.Angle);
        Assert.Equal(1, off.Shown);
    }

    [Fact]
    public void The_whirlpool_trails_to_the_click_and_stay_centered_keeps_it_in_the_middle()
    {
        var s = new AppSettings { SpiralOpacity = 60, WhirlpoolStayCentred = false };
        bool lit = false;
        using var scene = Laid(new SpiralPreviewScene(() => s, () => MotionLevel.Full, () => false, () => lit));
        scene.Click(new SKPoint(60, 200));   // not lit: nothing to move
        scene.Start();
        Run(scene, 1);
        Assert.False(scene.PoolOn);

        lit = true;
        Run(scene, 1);
        Assert.True(scene.PoolOn);
        Assert.True(scene.PoolRadius > 30, $"pool radius {scene.PoolRadius:0}");
        scene.Click(new SKPoint(80, 260));
        Run(scene, 2);
        Assert.InRange(scene.PoolAt.X, 70, 90);
        Assert.InRange(scene.PoolAt.Y, 250, 270);

        double r = scene.PoolRadius;
        s.WhirlpoolSize = 150;
        Run(scene, 0.1);
        Assert.True(scene.PoolRadius > r * 1.3);

        s.WhirlpoolStayCentred = true;
        scene.SettingChanged(nameof(AppSettings.WhirlpoolStayCentred));
        scene.Click(new SKPoint(40, 900));
        Run(scene, 2.5);
        Assert.InRange(scene.PoolAt.X, scene.Band.MidX - 6, scene.Band.MidX + 6);
        Assert.InRange(scene.PoolAt.Y, scene.Band.MidY - 6, scene.Band.MidY + 6);
    }

    // ---------------------------------------------------------------- pink filter

    private static SKColor RenderAt(IPreviewScene scene, int x, int y)
    {
        using var bmp = new SKBitmap(new SKImageInfo((int)Screen.Width, (int)Screen.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        scene.Render(c, Screen, Screen);
        c.Flush();
        return bmp.GetPixel(x, y);
    }

    [Fact]
    public void Pink_tints_in_the_filter_colour_at_its_opacity()
    {
        var s = new AppSettings { PinkFilterOpacity = 20, PinkFilterColor = "#00FF00" };
        using var scene = Laid(new PinkFilterPreviewScene(() => s, () => MotionLevel.Full, () => false, () => false, () => (255, 105, 180)));
        scene.Start();
        Run(scene, 2);
        var c = scene.TintNow;
        Assert.True(c.Green > 240 && c.Red < 10, $"tint {c}");
        Assert.InRange(c.Alpha, 45, 56);
        Assert.Equal(0, scene.Creep);

        s.PinkFilterColor = "";
        Run(scene, 2);
        Assert.True(scene.TintNow.Red > 240, "the mod / hot pink colour comes back when the pick is cleared");
        scene.Click(new SKPoint(100, 100));
        Assert.Equal(0, scene.OpenHoles);
    }

    [Fact]
    public void Steam_creeps_in_from_the_edges_by_its_reach_and_a_click_wipes_a_hole()
    {
        var s = new AppSettings { PinkFilterOpacity = 5, SteamReach = 10, SuperCreepCap = 0.9 };
        using var scene = Laid(new PinkFilterPreviewScene(() => s, () => MotionLevel.Full, () => false, () => true, () => (255, 105, 180)));
        scene.Start();
        Run(scene, 6);
        Assert.Equal(1, scene.Creep, 3);
        byte tintOnly = RenderAt(scene, 189, 539).Alpha;
        Assert.True(RenderAt(scene, 2, 539).Alpha > tintOnly + 40, "fog at the edge");
        Assert.True(RenderAt(scene, 189, 539).Alpha <= tintOnly + 2, "a thin edge line at reach 10");

        s.SteamReach = 100;
        Assert.True(RenderAt(scene, 189, 539).Alpha > tintOnly + 20, "the whole slice at reach 100");

        scene.Click(new SKPoint(20, 539));
        Assert.Equal(1, scene.OpenHoles);
        Assert.True(RenderAt(scene, 20, 539).Alpha < RenderAt(scene, 20, 700).Alpha);
        Run(scene, SteamFill(s));
        Assert.Equal(0, scene.OpenHoles);
    }

    private static double SteamFill(AppSettings s) => PinkFilterPreviewScene.ReturnSeconds * 100.0 / s.SteamFogReturn + 0.2;

    // ---------------------------------------------------------------- brain drain

    [Fact]
    public void Brain_drain_softens_by_blur_strength_and_melting_sways_the_bands()
    {
        var s = new AppSettings { BrainDrainBlurStrength = 0, BrainDrainMeltEnabled = false };
        using var scene = Laid(new BrainDrainPreviewScene(() => s, () => MotionLevel.Full, () => false, () => false));
        Run(scene, 1.5);
        Assert.Equal(0, scene.VeilOpacity);
        Assert.Equal(0, RenderAt(scene, 300, 600).Alpha);

        s.BrainDrainBlurStrength = 60;
        Assert.True(scene.VeilOpacity > 0.6);
        Assert.True(RenderAt(scene, 300, 600).Alpha > 100);
        Assert.Equal(0, scene.MeltShift(2));
        s.BrainDrainMeltEnabled = true;
        Run(scene, 0.7);
        Assert.NotEqual(0, scene.MeltShift(2));
        Assert.False(scene.LensOn);
        var lensAt = scene.LensAt;
        scene.Click(new SKPoint(50, 50));
        Run(scene, 1);
        Assert.Equal(lensAt, scene.LensAt);   // not lit: the click moves no lens
        Assert.False(scene.LensOn);
    }

    [Fact]
    public void The_undertow_lens_moves_to_the_click_widens_by_click_widen_and_drains()
    {
        var s = new AppSettings { BrainDrainBlurStrength = 70, UndertowClickWiden = 100, UndertowShrinkSpeed = 100 };
        using var scene = Laid(new BrainDrainPreviewScene(() => s, () => MotionLevel.Full, () => false, () => true));
        Run(scene, 1);
        Assert.True(scene.LensOn);
        // Clear inside the lens, soft outside it.
        var at = scene.LensAt;
        Assert.True(RenderAt(scene, (int)at.X, (int)at.Y).Alpha < 10);
        double r = scene.LensRadius;
        scene.Click(new SKPoint(100, 700));
        Assert.True(scene.LensRadius > r + 40);
        Run(scene, 1.5);
        Assert.InRange(scene.LensAt.Y, 680, 720);
        double wide = scene.LensRadius;
        Run(scene, 3);
        Assert.True(scene.LensRadius < wide, "it drains on its own");

        s.UndertowClickWiden = 0;
        double before = scene.LensRadius;
        scene.Click(new SKPoint(200, 300));
        Assert.Equal(before, scene.LensRadius, 3);
    }

    // ---------------------------------------------------------------- mind wipe

    [Fact]
    public void Mind_wipe_rings_follow_per_hour_volume_and_loop_and_a_click_sends_one_wipe()
    {
        var s = new AppSettings { MindWipeFrequency = 1, MindWipeVolume = 20, MindWipeLoop = false };
        using var slow = Laid(new MindWipePreviewScene(() => s, () => MotionLevel.Full, () => false));
        slow.Start();
        Run(slow, 10);
        int few = slow.RingsSent;

        var f = new AppSettings { MindWipeFrequency = 180, MindWipeVolume = 20 };
        using var fast = Laid(new MindWipePreviewScene(() => f, () => MotionLevel.Full, () => false));
        fast.Start();
        Run(fast, 10);
        Assert.True(fast.RingsSent >= few * 3, $"{fast.RingsSent} rings at 180 an hour, {few} at 1");

        float small = slow.MaxRadius;
        s.MindWipeVolume = 100;
        Assert.True(slow.MaxRadius > small * 2);

        int sent = slow.RingsSent;
        slow.Click(new SKPoint(10, 10));
        Assert.Equal(sent + MindWipePreviewScene.RingsPerWipe, slow.RingsSent);

        var l = new AppSettings { MindWipeLoop = true, MindWipeFrequency = 1 };
        using var loop = Laid(new MindWipePreviewScene(() => l, () => MotionLevel.Full, () => false));
        loop.Start();
        Run(loop, 10);
        Assert.InRange(loop.RingsSent, 8, 10);   // a steady pulse every 1.1 s
    }

    // ---------------------------------------------------------------- renders

    [Fact]
    [Trait("Category", "Render")]
    public void Each_scene_renders_a_lively_moment_and_dumps_when_asked()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            EffectPreview.DesktopScaleOverride = 1.5;
            try
            {
                var dir = Environment.GetEnvironmentVariable("CCP_ROWS_DUMP");
                var empty = new EffectPreview { Width = 400, Height = 1100 };
                Layout(empty);
                var emptyShot = Shot(empty, 144);

                var s = new AppSettings
                {
                    SpiralOpacity = 60, WhirlpoolSize = 110,
                    PinkFilterOpacity = 18, SteamReach = 60, SuperCreepCap = 0.8,
                    BrainDrainBlurStrength = 70, BrainDrainMeltEnabled = true,
                    MindWipeFrequency = 120, MindWipeVolume = 80,
                };
                foreach (var id in new[] { "spiral", "pinkfilter", "braindrain", "mindwipe" })
                {
                    var scene = Make(id, s, lit: id != "mindwipe");
                    var busy = new EffectPreview { Width = 400, Height = 1100 };
                    busy.UseScene(scene);
                    busy.RasterScale = 0.75;   // what half resolution gives on a 150% screen
                    Layout(busy);
                    busy.Driver!.Open();
                    Run(scene, 1.2);
                    scene.Click(new SKPoint(150, 380));
                    Run(scene, id == "mindwipe" ? 0.55 : 1.6);
                    busy.Repaint();
                    var shot = Shot(busy, 144);
                    if (dir is { Length: > 0 }) { Directory.CreateDirectory(dir); Save(shot, Path.Combine(dir, id + "-preview.png")); }
                    // Something of the scene is on the slice: the band near the click differs from the bare desktop.
                    var probe = scene is MindWipePreviewScene m ? m.Speaker : new SKPoint(150, 380 + 400);
                    int px = (int)((probe.X + PreviewDesktop.Bezel) * 1.5), py = (int)((probe.Y + PreviewDesktop.Bezel) * 1.5);
                    Assert.NotEqual(Pixel(emptyShot, px, py), Pixel(shot, px, py));
                    scene.Dispose();
                }
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

    private static void Save(BitmapSource b, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
