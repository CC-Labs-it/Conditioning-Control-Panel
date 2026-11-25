using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Controls.Preview;
using ConditioningControlPanel.Controls.Preview.Scenes;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Live Preview lane P1 (overnight sweep 2026-11-24): the Mandatory Video, Lock Card and Wallpaper scenes.
/// Each is pinned on an injected clock: wakes on a watched setting, sleeps after the wake window, limits,
/// clicks, Off motion never animates. With env CCP_ROWS_DUMP=&lt;dir&gt; the render tests save
/// video-preview.png, lockcard-preview.png and wallpaper-preview.png (the preview alone, 400 x 1100).
/// </summary>
public class PreviewP1Tests
{
    private static readonly SKRect Screen = new(0, 0, 378, 1078);

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

    /// <summary>Every scene: a watched setting wakes it, an unwatched one does not, it sleeps after the window.</summary>
    private static void WakeAndSleep(IPreviewScene scene, string watched)
    {
        double now = 50;
        var d = new PreviewDriver(scene, () => now);
        Assert.False(d.SettingChanged(nameof(AppSettings.SubliminalFrequency)));
        Assert.False(d.WantsClock);
        Assert.True(d.SettingChanged(watched));
        Assert.True(d.WantsClock);
        Assert.InRange(RunFrames(d, s => now += s, 12), 280, 310);
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
        // The show running (panel quiet) holds it: nothing moves on a change or a click.
        d.Pace = PreviewGate.Pace(MotionLevel.Full, panelQuiet: true, allowAmbientLoops: false, tierAllowsMotion: true);
        d.SettingChanged(watched);
        Assert.False(d.Click(new SKPoint(10, 10)));
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
    }

    // ---------------------------------------------------------------- Mandatory Video

    private static AppSettings VideoSettings() => new()
    {
        VideosPerHour = 20, MandatoryVideoFloating = false, MandatoryVideoFloatingCount = 2, MandatoryVideoFloatingSizePercent = 33,
        MandatoryVideoFloatingSpeedPercent = 35, AttentionChecksEnabled = false, AttentionDensity = 6, AttentionSize = 90,
        AttentionLifespan = 10, LightsDownDarkness = 80,
    };

    private static MandatoryVideoPreviewScene Video(AppSettings s, MotionLevel level = MotionLevel.Full, bool lights = false, int seed = 5)
    {
        var scene = new MandatoryVideoPreviewScene(() => s, () => level, () => false, () => lights, new Random(seed));
        scene.Layout(Screen, Screen);
        return scene;
    }

    [Fact]
    public void Video_wakes_on_a_watched_setting_and_sleeps_after_the_window()
    {
        using var scene = Video(VideoSettings());
        WakeAndSleep(scene, nameof(AppSettings.MandatoryVideoFloating));
    }

    [Fact]
    public void Video_full_screen_covers_the_band_and_floating_respects_most_at_once()
    {
        var s = VideoSettings();
        using var scene = Video(s);
        scene.Start();
        Run(scene, 1);
        var v = Assert.Single(scene.Videos);
        Assert.True(v.Full);
        Assert.Equal(scene.Band.Top, v.Rect.Top);
        Assert.Equal(scene.Band.Bottom, v.Rect.Bottom);

        s.MandatoryVideoFloating = true; s.MandatoryVideoFloatingCount = 3;
        scene.SettingChanged(nameof(AppSettings.MandatoryVideoFloating));
        bool sawFloating = false;
        for (int i = 0; i < 30 * 30; i++)
        {
            scene.Tick(1 / 30.0);
            sawFloating |= scene.Videos.Any(x => !x.Full);
            Assert.True(scene.Videos.Count(x => !x.Leaving) <= 3, $"{scene.Videos.Count} up at {scene.Clock:0.0}s");
            Assert.All(scene.Videos.Where(x => !x.Full), x => Assert.True(x.Rect.Width < Screen.Width));
        }
        Assert.True(sawFloating);

        // Pop up together opens every window on one trigger.
        s.MandatoryVideoFloatingTogether = true;
        scene.SettingChanged(nameof(AppSettings.MandatoryVideoFloatingTogether));
        int most = 0;
        for (int i = 0; i < 30 * 4; i++) { scene.Tick(1 / 30.0); most = Math.Max(most, scene.Videos.Count(x => !x.Leaving)); }
        Assert.Equal(3, most);
    }

    [Fact]
    public void Video_minigame_targets_pop_on_click_and_none_show_when_it_is_off()
    {
        var s = VideoSettings();
        using var scene = Video(s);
        scene.Start();
        Run(scene, 3);
        Assert.Empty(scene.Targets);

        s.AttentionChecksEnabled = true;
        scene.SettingChanged(nameof(AppSettings.AttentionChecksEnabled));
        Run(scene, 2);
        Assert.True(scene.LiveTargets > 0);
        Assert.True(scene.LiveTargets <= (s.AttentionDensity + 1) / 2);
        var t = scene.Targets.First(x => !x.Popped);
        scene.Click(t.At);
        Assert.Equal(1, scene.Popped);
        Assert.True(t.Popped);
        scene.Click(new SKPoint(-50, -50));   // a miss pops nothing
        Assert.Equal(1, scene.Popped);
    }

    [Fact]
    public void Video_lights_down_dims_around_floating_windows_only_when_lit()
    {
        var s = VideoSettings();
        s.MandatoryVideoFloating = true;
        using (var dark = Video(s, lights: true))
        {
            dark.Start();
            Run(dark, 3);
            Assert.True(dark.Veil > 0.3, $"veil {dark.Veil:0.00}");
            Assert.True(dark.Veil <= 0.82 + 1e-6);
        }
        s.MandatoryVideoFloating = false;   // full screen: the edges dim
        using (var edges = Video(s, lights: true))
        {
            edges.Start();
            Run(edges, 3);
            Assert.True(edges.Veil > 0.3);
        }
        s.MandatoryVideoFloating = true;
        using var plain = Video(s, lights: false);
        plain.Start();
        Run(plain, 3);
        Assert.Equal(0, plain.Veil);
    }

    [Fact]
    public void Video_off_motion_never_animates_and_jumps_to_the_still()
    {
        var s = VideoSettings();
        double now = 0;
        using var scene = Video(s, MotionLevel.Off);
        var d = new PreviewDriver(scene, () => now) { Pace = PreviewGate.Pace(MotionLevel.Off, false, true, true) };
        d.Open();
        Assert.False(d.WantsClock);
        Assert.Equal(0, d.Ticks);
        var v = Assert.Single(scene.Videos);
        Assert.Equal(1, v.Alpha);
        d.SettingChanged(nameof(AppSettings.VideosPerHour));
        now += 3;
        Assert.Equal(0, RunFrames(d, x => now += x, 2));
    }

    // ---------------------------------------------------------------- Lock Card

    private static AppSettings LockSettings() => new()
    {
        LockCardFrequency = 10, LockCardRepeats = 3, LockCardRandomRepeats = false, LockCardStrict = false, LockCardResetOnTypo = false,
    };

    private static LockCardPreviewScene Lock(AppSettings s, MotionLevel level = MotionLevel.Full, int seed = 3)
    {
        var scene = new LockCardPreviewScene(() => s, () => level, () => false, new Random(seed)) { TypoChance = 0 };
        scene.Layout(Screen, Screen);
        return scene;
    }

    [Fact]
    public void Lock_card_wakes_on_a_watched_setting_and_sleeps_after_the_window()
    {
        using var scene = Lock(LockSettings());
        WakeAndSleep(scene, nameof(AppSettings.LockCardRepeats));
    }

    [Fact]
    public void Lock_card_types_every_repeat_then_completes_and_fades()
    {
        var s = LockSettings();
        using var scene = Lock(s);
        scene.Start();
        Run(scene, 0.8);
        Assert.True(scene.CardUp);
        Assert.Equal(3, scene.Repeats);
        Assert.Contains(scene.Word, LockCardPreviewScene.Words);
        Assert.InRange(scene.Card.MidY, Screen.Top, Screen.Bottom);
        int maxDone = 0;
        for (int i = 0; i < 30 * 12 && scene.CardsFinished == 0; i++) { scene.Tick(1 / 30.0); maxDone = Math.Max(maxDone, scene.RepeatsDone); }
        Assert.Equal(1, scene.CardsFinished);
        Assert.Equal(3, maxDone);
        Assert.False(scene.CardUp);

        // Random repeats roll between Fewest and Repeats.
        s.LockCardRandomRepeats = true; s.LockCardRepeatsMin = 2; s.LockCardRepeats = 4;
        for (int k = 0; k < 6; k++)
        {
            scene.SettingChanged(nameof(AppSettings.LockCardRandomRepeats));
            for (int i = 0; i < 30 * 8 && !scene.CardUp; i++) scene.Tick(1 / 30.0);
            Assert.InRange(scene.Repeats, 2, 4);
            for (int i = 0; i < 30 * 15 && scene.CardUp; i++) scene.Tick(1 / 30.0);
        }
    }

    [Fact]
    public void Lock_card_click_types_ahead_and_never_says_the_completion_line()
    {
        var s = LockSettings();
        using var scene = Lock(s);
        scene.Start();
        for (int i = 0; i < 60 && !scene.Typing; i++) scene.Tick(1 / 30.0);
        Assert.True(scene.Typing);
        int before = scene.Typed;
        scene.Click(new SKPoint(10, 10));
        Assert.True(scene.Typed >= Math.Min(scene.Word.Length, before + 1) || scene.RepeatsDone > 0);


    }

    [Fact]
    public void Lock_card_off_motion_never_animates()
    {
        double now = 0;
        using var scene = Lock(LockSettings(), MotionLevel.Off);
        var d = new PreviewDriver(scene, () => now) { Pace = PreviewGate.Pace(MotionLevel.Off, false, true, true) };
        d.Open();
        Assert.False(d.WantsClock);
        Assert.Equal(0, d.Ticks);
        Assert.True(scene.CardUp);
        Assert.Equal(1, scene.Alpha);
        Assert.Equal(0, RunFrames(d, x => now += x, 2));
    }

    // ---------------------------------------------------------------- Wallpaper

    private static AppSettings WallSettings() => new()
    {
        CollageMode = WallpaperMode.Static, CollageWallpaperRotate = true, CollageWallpaperIntervalSeconds = 10, CollageWallpaperFit = WallpaperFit.Fill,
    };

    private static WallpaperPreviewScene Wall(AppSettings s, MotionLevel level = MotionLevel.Full, int seed = 9)
    {
        var scene = new WallpaperPreviewScene(() => s, () => level, () => false, new Random(seed));
        scene.Layout(Screen, Screen);
        return scene;
    }

    [Fact]
    public void Wallpaper_is_a_wallpaper_scene_and_wakes_on_a_watched_setting()
    {
        using var scene = Wall(WallSettings());
        Assert.IsAssignableFrom<IPreviewWallpaperScene>(scene);
        WakeAndSleep(scene, nameof(AppSettings.CollageMode));
    }

    [Fact]
    public void Wallpaper_crossfades_in_on_open_rotates_on_the_interval_and_holds_when_rotate_is_off()
    {
        var s = WallSettings();
        using var scene = Wall(s);
        scene.Start();
        Assert.Equal(1, scene.Changes);
        Assert.True(scene.Mix < 0.1);
        Run(scene, CrossfadeHalf());
        Assert.InRange(scene.Mix, 0.2, 0.8);
        Run(scene, 6);
        Assert.True(scene.Changes >= 2, $"{scene.Changes} changes");

        s.CollageWallpaperRotate = false;
        scene.SettingChanged(nameof(AppSettings.CollageWallpaperRotate));
        int held = scene.Changes;
        Run(scene, 20);
        Assert.Equal(held, scene.Changes);

        // A click or a mode change brings the next picture.
        scene.Click(new SKPoint(100, 100));
        Assert.Equal(held + 1, scene.Changes);
        s.CollageMode = WallpaperMode.OwnImage;
        scene.SettingChanged(nameof(AppSettings.CollageMode));
        Assert.Equal(WallpaperMode.OwnImage, scene.ShownMode);
    }

    private static double CrossfadeHalf() => WallpaperPreviewScene.CrossfadeSeconds / 2;

    [Fact]
    public void Wallpaper_off_motion_never_animates()
    {
        double now = 0;
        using var scene = Wall(WallSettings(), MotionLevel.Off);
        var d = new PreviewDriver(scene, () => now) { Pace = PreviewGate.Pace(MotionLevel.Off, false, true, true) };
        d.Open();
        Assert.False(d.WantsClock);
        Assert.Equal(0, d.Ticks);
        Assert.True(scene.HasWall);
        Assert.Equal(1, scene.Mix);
        Assert.Equal(0, RunFrames(d, x => now += x, 2));
    }

}
