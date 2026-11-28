using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Preview;
using ConditioningControlPanel.Controls.Preview.Scenes;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Tabs;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Live Preview lane P5 (second batch 2026-11-24): the Takeover scene, pinned on an injected clock (wakes on
/// a watched setting, sleeps after the wake window, a beat whose switch is off never draws, a click starts a
/// takeover, the wallpaper comes back unless Keep is on, Motion Off never animates). With env
/// CCP_ROWS_DUMP=&lt;dir&gt; the render tests save takeover-preview.png (the preview alone at the card's real
/// size) and takeover.png (the whole tab at 900 px).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PreviewP5Tests
{
    /// <summary>The card: 300 tall with a 2 px border, in a side column about 300 wide.</summary>
    private const double CardW = 296, CardH = 296;
    private static readonly SKRect Screen = new(0, 0, (float)(CardW - 2 * PreviewDesktop.Bezel), (float)(CardH - 2 * PreviewDesktop.Bezel));

    private static AppSettings Everything() => new()
    {
        AutonomyRandomIntervalSeconds = 30, AutonomyCooldownSeconds = 10, AutonomyIntensity = 7,
        AutonomyAnnouncementChance = 100, AutonomyRandomTriggerEnabled = true, AutonomyIdleTriggerEnabled = false,
        AutonomyCanTriggerWebVideo = true, AutonomyCanTriggerWallpaper = true, WallpaperEnabled = false, WallpaperPulseSeconds = 10,
        AutonomyCanTriggerVoiceCommand = true, MicConsentGiven = true, MantraChantEnabled = true, MantraChantGapSeconds = 5,
        MantraChantVolume = 60, ShowTakeoverCountdownBar = true,
    };

    private static AppSettings Nothing() => new()
    {
        AutonomyAnnouncementChance = 0, AutonomyRandomTriggerEnabled = true, AutonomyCanTriggerWebVideo = false,
        AutonomyCanTriggerWallpaper = false, AutonomyCanTriggerVoiceCommand = false, MicConsentGiven = true,
        MantraChantEnabled = false, ShowTakeoverCountdownBar = false, AutonomyRandomIntervalSeconds = 30, AutonomyCooldownSeconds = 10,
    };

    private static TakeoverPreviewScene Scene(AppSettings s, MotionLevel level = MotionLevel.Full, int seed = 3)
    {
        var scene = new TakeoverPreviewScene(() => s, () => level, () => false, new Random(seed));
        scene.Layout(Screen, Screen);
        return scene;
    }

    private static void Run(IPreviewScene scene, double seconds, Action? each = null)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) { scene.Tick(1 / 30.0); each?.Invoke(); }
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

    [Fact]
    public void The_id_takeover_finds_the_class_and_it_has_a_real_parameterless_constructor()
    {
        Assert.NotNull(typeof(TakeoverPreviewScene).GetConstructor(Type.EmptyTypes));
        WpfRenderHarness.OnStaThread(() =>
        {
            var pv = new EffectPreview { Scene = "takeover" };
            Assert.IsType<TakeoverPreviewScene>(pv.Driver!.Scene);
            Assert.IsNotAssignableFrom<IPreviewWallpaperScene>(pv.Driver.Scene);   // it paints its own wall AND the browser over it
        });
    }

    [Fact]
    public void It_wakes_on_a_watched_setting_and_sleeps_after_the_window()
    {
        using var scene = Scene(Everything());
        double now = 50;
        var d = new PreviewDriver(scene, () => now);
        foreach (var n in new[] { nameof(AppSettings.AutonomyCanTriggerWebVideo), nameof(AppSettings.WallpaperEnabled),
                     nameof(AppSettings.MantraChantEnabled), nameof(AppSettings.ShowTakeoverCountdownBar), nameof(AppSettings.AutonomyRandomIntervalSeconds) })
            Assert.True(d.Watches(n), n);
        Assert.False(d.SettingChanged(nameof(AppSettings.SubliminalFrequency)));
        Assert.False(d.WantsClock);
        Assert.True(d.SettingChanged(nameof(AppSettings.AutonomyCanTriggerWebVideo)));
        Assert.True(d.WantsClock);
        Assert.InRange(RunFrames(d, s => now += s, 12), 280, 310);
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
        Assert.True(scene.Takeovers >= 1);   // the change played a takeover inside the window
        // The show running holds it: nothing moves on a change or a click.
        d.Pace = PreviewGate.Pace(MotionLevel.Full, panelQuiet: true, allowAmbientLoops: false, tierAllowsMotion: true);
        d.SettingChanged(nameof(AppSettings.MantraChantEnabled));
        Assert.False(d.Click(new SKPoint(100, 100)));
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
    }

    [Fact]
    public void Every_beat_plays_when_its_switch_is_on()
    {
        using var scene = Scene(Everything());
        scene.Start();
        double notice = 0, grow = 0, mantra = 0, chant = 0, wall = 0, countdown = 0;
        Run(scene, 8, () =>
        {
            notice = Math.Max(notice, scene.Notice); grow = Math.Max(grow, scene.Grow); mantra = Math.Max(mantra, scene.Mantra);
            chant = Math.Max(chant, scene.Chant); wall = Math.Max(wall, scene.WallMix); countdown = Math.Max(countdown, scene.Countdown);
        });
        Assert.True(scene.Takeovers >= 1);
        Assert.True(notice > 0.95, $"notice {notice}");
        Assert.True(grow > 0.95, $"grow {grow}");
        Assert.True(mantra > 0.95, $"mantra {mantra}");
        Assert.True(chant > 0.3, $"chant {chant}");
        Assert.True(wall > 0.95, $"wall {wall}");
        Assert.True(countdown > 0.95, $"countdown {countdown}");
        Assert.Contains(scene.Word, TakeoverPreviewScene.Words);
    }

    [Fact]
    public void A_beat_whose_switch_is_off_never_draws()
    {
        using var scene = Scene(Nothing());
        scene.Start();
        Run(scene, 20, () =>
        {
            Assert.Equal(0, scene.Notice);
            Assert.Equal(0, scene.Grow);
            Assert.Equal(0, scene.Mantra);
            Assert.Equal(0, scene.Chant);
            Assert.Equal(0, scene.WallMix);
            Assert.Equal(0, scene.Countdown);
        });
        Assert.True(scene.Takeovers >= 2);   // the takeovers still came, each with nothing to show

        // Mantras need the mic consent as the tab's switch reads it.
        var s = Nothing();
        s.AutonomyCanTriggerVoiceCommand = true; s.MicConsentGiven = false;
        using var noMic = Scene(s);
        noMic.Start();
        Run(noMic, 8, () => Assert.Equal(0, noMic.Mantra));
    }

    [Fact]
    public void A_switch_turned_off_mid_takeover_fades_its_beat_out()
    {
        var s = Everything();
        using var scene = Scene(s);
        scene.Click(new SKPoint(50, 50));
        Run(scene, 2.6);
        Assert.True(scene.Grow > 0.9);
        s.AutonomyCanTriggerWebVideo = false;
        scene.SettingChanged(nameof(AppSettings.AutonomyCanTriggerWebVideo));
        scene.Tick(1 / 30.0);
        Assert.True(scene.Grow > 0.5, "no cut: it eases back");
        Run(scene, 0.5);
        Assert.Equal(0, scene.Grow);
    }

    [Fact]
    public void A_click_starts_a_takeover_now_and_not_twice_at_once()
    {
        var s = Everything();
        s.AutonomyRandomTriggerEnabled = false;
        using var scene = Scene(s);
        Run(scene, 5);
        Assert.Equal(0, scene.Takeovers);          // no trigger on, not opened: nothing comes by itself
        scene.Click(new SKPoint(120, 120));
        Assert.True(scene.InTakeover);
        Assert.Equal(1, scene.Takeovers);
        scene.Click(new SKPoint(120, 120));
        Assert.Equal(1, scene.Takeovers);
        Run(scene, 10);
        Assert.False(scene.InTakeover);
        Assert.Equal(1, scene.Takeovers);           // and with no trigger on, none follows
        Assert.True(double.IsPositiveInfinity(scene.NextAt));
    }

    [Fact]
    public void The_interval_sets_the_wait_to_the_next_takeover()
    {
        double Wait(int interval)
        {
            var s = Nothing();
            s.AutonomyRandomIntervalSeconds = interval; s.AutonomyCooldownSeconds = 10;
            using var scene = Scene(s);
            scene.Click(default);
            while (scene.InTakeover) scene.Tick(1 / 30.0);
            return scene.NextAt - scene.Clock;
        }
        double quick = Wait(30), slow = Wait(300);
        Assert.InRange(quick, 1.9, 2.1);
        Assert.InRange(slow, 5.9, 6.1);
    }

    [Fact]
    public void The_wallpaper_comes_back_unless_keep_is_on()
    {
        var s = Everything();
        s.AutonomyRandomTriggerEnabled = false; s.WallpaperPulseSeconds = 10;
        using var scene = Scene(s);
        scene.Click(default);
        Run(scene, 2.5);
        Assert.Equal(1, scene.WallMix);
        Run(scene, 6);
        Assert.False(scene.InTakeover);
        Assert.Equal(0, scene.WallMix);

        s.WallpaperEnabled = true;   // Keep
        using var kept = Scene(s);
        kept.Click(default);
        Run(kept, 15);
        Assert.False(kept.InTakeover);
        Assert.Equal(1, kept.WallMix);
        // Keep turned off: it fades back after the pulse.
        s.WallpaperEnabled = false;
        kept.SettingChanged(nameof(AppSettings.WallpaperEnabled));
        Run(kept, 9);
        Assert.Equal(0, kept.WallMix);
    }

    [Fact]
    public void Motion_off_is_a_still_of_the_middle_and_never_animates()
    {
        using var scene = Scene(Everything(), MotionLevel.Off);
        double now = 0;
        var d = new PreviewDriver(scene, () => now)
        {
            Pace = PreviewGate.Pace(MotionLevel.Off, panelQuiet: false, allowAmbientLoops: true, tierAllowsMotion: true),
        };
        Assert.True(d.Open());
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 5));
        double clock = scene.Clock;
        Assert.True(scene.InTakeover);
        Assert.Equal(1, scene.Grow);
        Assert.Equal(1, scene.Mantra);
        Assert.Equal(1, scene.WallMix);
        Assert.Equal(0, scene.Notice);
        // A settle runs the scene but nothing in it moves.
        d.SettingChanged(nameof(AppSettings.MantraChantVolume));
        d.Click(new SKPoint(20, 20));
        Assert.Equal(clock, scene.Clock);
        Assert.Equal(1, scene.Takeovers);
        Assert.Equal(1, scene.Grow);
    }

    [Fact]
    [Trait("Category", "Render")]
    public void The_preview_renders_mid_takeover_and_dumps_when_asked()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            EffectPreview.DesktopScaleOverride = 1.5;
            try
            {
                var dir = Environment.GetEnvironmentVariable("CCP_ROWS_DUMP");
                var plain = new EffectPreview { Width = CardW, Height = CardH };
                Layout(plain);
                var empty = Shot(plain, 144);

                // Mid takeover with every beat on, the window half grown so the browser and the wall both show.
                var s = Everything();
                var scene = new TakeoverPreviewScene(() => s, () => MotionLevel.Full, () => false, new Random(11));
                var host = new EffectPreview { Width = CardW, Height = CardH };
                host.UseScene(scene);
                host.RasterScale = 0.75;
                Layout(host);
                host.Driver!.Open();
                Run(scene, 0.9);                  // the first takeover begins
                Assert.True(scene.InTakeover);
                Run(scene, 2.75);                 // notice gone, wall up, window full, mantra and chant on
                Assert.True(scene.Grow > 0.9 && scene.Mantra > 0.9 && scene.WallMix > 0.9);
                host.Repaint();
                var shot = Shot(host, 144);
                if (dir is { Length: > 0 }) { Directory.CreateDirectory(dir); Save(shot, Path.Combine(dir, "takeover-preview.png")); }
                Assert.NotEqual(Pixel(empty, (int)(CardW * 0.75), (int)(CardH * 0.75)), Pixel(shot, (int)(CardW * 0.75), (int)(CardH * 0.75)));

                // The rest pose with the notice in, for a second look.
                var s2 = Everything();
                s2.AutonomyCanTriggerWallpaper = false;
                var rest = new TakeoverPreviewScene(() => s2, () => MotionLevel.Full, () => false, new Random(12));
                var host2 = new EffectPreview { Width = CardW, Height = CardH };
                host2.UseScene(rest);
                host2.RasterScale = 0.75;
                Layout(host2);
                host2.Driver!.Open();
                Run(rest, 1.3);
                Assert.True(rest.Notice > 0.9);
                host2.Repaint();
                var shot2 = Shot(host2, 144);
                if (dir is { Length: > 0 }) Save(shot2, Path.Combine(dir, "takeover-preview-notice.png"));
                // Web video off: the window stays, the wallpaper under the icons has changed, the word is up.
                var s3 = Everything();
                s3.AutonomyCanTriggerWebVideo = false;
                var wall = new TakeoverPreviewScene(() => s3, () => MotionLevel.Full, () => false, new Random(13));
                var host3 = new EffectPreview { Width = CardW, Height = CardH };
                host3.UseScene(wall);
                host3.RasterScale = 0.75;
                Layout(host3);
                host3.Driver!.Open();
                Run(wall, 3.6);
                Assert.True(wall.WallMix > 0.9 && wall.Mantra > 0.9 && wall.Grow == 0);
                host3.Repaint();
                var shot3 = Shot(host3, 144);
                if (dir is { Length: > 0 }) Save(shot3, Path.Combine(dir, "takeover-preview-wall.png"));
                scene.Dispose(); rest.Dispose(); wall.Dispose();
            }
            finally { EffectPreview.DesktopScaleOverride = null; }
        });
    }

    [Fact]
    [Trait("Category", "Render")]
    public void The_takeover_tab_holds_the_preview_and_dumps_when_asked()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new BambiTakeoverTabView();
            var host = RowsRender.Host(page);
            var pv = RowsRender.Descendants(page).OfType<EffectPreview>().Single();
            Assert.IsType<TakeoverPreviewScene>(pv.Driver!.Scene);
            Assert.InRange(pv.ActualHeight, 280, 300);   // the card's fixed height reaches the slice
            Assert.NotNull(page.FindName("TakeoverSideArtBrush"));
            RowsRender.Dump(host, "takeover");
            RowsRender.AssertSilent("takeover");

            // At 900 the tab folds its side rail away (AdaptiveSideArt.CollapseBelow="900"), so a wider still
            // shows the card in its column.
            var wide = new BambiTakeoverTabView();
            var wideHost = RowsRender.Host(wide, 1200);
            var wpv = RowsRender.Descendants(wide).OfType<EffectPreview>().Single();
            Assert.True(wpv.ActualWidth > 200, $"preview width {wpv.ActualWidth}");
            RowsRender.Dump(wideHost, "takeover-1200");
            RowsRender.AssertSilent("takeover-1200");
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
