using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Preview;
using ConditioningControlPanel.Controls.Preview.Scenes;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Live Preview sweep lane P2 (owner 2026-11-24): the Bubbles, Subliminals, Bubble Count and Bouncing
/// Text scenes. Each scene is pinned on an injected clock (wakes on a watched setting, sleeps after the
/// wake window, Off motion never animates), its limits and its click. With env CCP_ROWS_DUMP=&lt;dir&gt;
/// the render test saves &lt;page&gt;-preview.png (the preview alone, 400 x 1100, 144 dpi).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PreviewP2Tests
{
    private static readonly SKRect Screen = new(0, 0, 378, 1078);

    private static T Laid<T>(T scene, SKRect? visible = null) where T : IPreviewScene
    {
        scene.Layout(Screen, visible ?? Screen);
        return scene;
    }

    private static void Run(IPreviewScene scene, double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0) scene.Tick(1 / 30.0);
    }

    /// <summary>The shared wake rule, through the real driver on an injected clock, for one scene and one of its settings.</summary>
    private static void WakesAndSleeps(IPreviewScene scene, string watched)
    {
        double now = 50;
        var d = new PreviewDriver(scene, () => now);
        Assert.False(d.WantsClock);
        Assert.False(d.SettingChanged(nameof(AppSettings.FlashFrequency)));   // none of these scenes watches Flash
        Assert.False(d.WantsClock);
        Assert.True(d.SettingChanged(watched));
        Assert.True(d.WantsClock);
        int frames = 0;
        for (double t = 0; t < PreviewDriver.WakeSeconds + 3; t += 1 / 30.0)
        {
            now += 1 / 30.0;
            if (d.WantsClock && d.Frame()) frames++;
        }
        Assert.InRange(frames, 280, 310);
        Assert.False(d.WantsClock);
        // A click wakes it again.
        d.Click(new SKPoint(5, 5));
        Assert.True(d.WantsClock);
    }

    private static void OffNeverAnimates(IPreviewScene scene, string watched)
    {
        double now = 0;
        var d = new PreviewDriver(scene, () => now) { Pace = PreviewGate.Pace(MotionLevel.Off, false, true, true) };
        d.Open();
        d.SettingChanged(watched);
        now += 4;
        Assert.False(d.WantsClock);
        Assert.False(d.Frame());
        Assert.Equal(0, d.Ticks);
    }

    // ---------------------------------------------------------------- Bubbles

    private static AppSettings BubbleSettings() => new()
    {
        BubblesFrequency = 60, BubbleSpeedBoost = 0, BubblesSize = 100, BubblesClickable = true,
        BubbleMotionStyle = BubbleMotionStyle.FloatUp,
    };

    private static BubblesPreviewScene Bubbles(AppSettings s, MotionLevel level = MotionLevel.Full, bool froth = false, int seed = 5, SKRect? visible = null)
        => Laid(new BubblesPreviewScene(() => s, () => level, () => false, _ => false, () => froth, new Random(seed)), visible);

    [Fact]
    public void Bubbles_wake_on_a_watched_setting_and_sleep_after_the_window()
    {
        var s = BubbleSettings();
        using var scene = Bubbles(s);
        WakesAndSleeps(scene, nameof(AppSettings.BubblesFrequency));
        using var off = Bubbles(s, MotionLevel.Off);
        OffNeverAnimates(off, nameof(AppSettings.BubblesSize));
        Assert.True(off.OnScreen > 0);   // the settled still has bubbles in it
    }

    [Fact]
    public void Bubbles_rise_in_the_visible_band_never_past_the_limit_and_per_min_sets_the_pace()
    {
        var s = BubbleSettings();
        var band = new SKRect(0, 400, 378, 960);
        using var scene = Bubbles(s, visible: band);
        scene.Start();
        for (int i = 0; i < 600; i++)
        {
            scene.Tick(1 / 30.0);
            Assert.True(scene.OnScreen <= BubblesPreviewScene.MaxBubbles);
            Assert.All(scene.Items, it => Assert.InRange(it.CenterYPx, band.Top - it.SizeDip, band.Bottom + it.SizeDip));
        }
        Assert.True(scene.OnScreen >= 4);

        // 1 a minute: far fewer in the same time.
        s.BubblesFrequency = 1;
        using var slow = Bubbles(s);
        slow.Start();
        Run(slow, 4);
        Assert.InRange(slow.OnScreen, 1, 2);

        // Speed boost: the same bubble climbs further in a second.
        var fast = BubbleSettings(); fast.BubbleSpeedBoost = 400; fast.BubblesFrequency = 1;
        var calm = BubbleSettings(); calm.BubblesFrequency = 1;
        using var plainRise = Bubbles(calm, seed: 9);
        using var fastRise = Bubbles(fast, seed: 9);
        plainRise.Start(); fastRise.Start();
        Run(plainRise, 0.6); Run(fastRise, 0.6);
        Assert.True(fastRise.Items.Single().CenterYPx < plainRise.Items.Single().CenterYPx - 50);
    }

    [Fact]
    public void A_bubble_pops_on_a_click_only_while_clickable_and_froth_merges_touching_bubbles()
    {
        var s = BubbleSettings(); s.BubblesFrequency = 1;
        using var scene = Bubbles(s);
        scene.Start();
        Run(scene, 0.8);
        var item = scene.Items.Single();
        s.BubblesClickable = false;
        scene.Click(new SKPoint((float)item.CenterXPx, (float)item.CenterYPx));
        Assert.Equal(1, scene.OnScreen);
        s.BubblesClickable = true;
        scene.Click(new SKPoint((float)item.CenterXPx, (float)item.CenterYPx));
        Assert.Equal(0, scene.OnScreen);

        // Froth lit: a busy field grows merged bubbles, never past the gold box's biggest size.
        var f = BubbleSettings(); f.BubblesSize = 150; f.FrothMaxSize = 2.5;
        using var froth = Bubbles(f, froth: true, visible: new SKRect(0, 0, 378, 300));
        froth.Start();
        Run(froth, 14);
        Assert.True(froth.Merged > 0);
        Assert.All(froth.Items, i => Assert.True(i.FrothScale <= 2.5f + 1e-3));
        using var plain = Bubbles(f, froth: false, visible: new SKRect(0, 0, 378, 300));
        plain.Start();
        Run(plain, 14);
        Assert.Equal(0, plain.Merged);
    }

    // ---------------------------------------------------------------- Bouncing Text

    private static AppSettings TextSettings() => new()
    {
        BouncingTextSpeed = 5, BouncingTextSize = 100, BouncingTextOpacity = 100, BouncingTextColorMode = 0,
        BouncingTextSecondText = false,
    };

    private static BouncingTextPreviewScene Text(AppSettings s, MotionLevel level = MotionLevel.Full, bool scrawl = false, SKRect? visible = null)
        => Laid(new BouncingTextPreviewScene(() => s, () => level, () => false, () => scrawl, new Random(3)), visible);

    [Fact]
    public void Bouncing_text_wakes_and_sleeps_and_off_never_animates()
    {
        using var scene = Text(TextSettings());
        WakesAndSleeps(scene, nameof(AppSettings.BouncingTextSpeed));
        using var off = Text(TextSettings(), MotionLevel.Off);
        OffNeverAnimates(off, nameof(AppSettings.BouncingTextSize));
    }

    [Fact]
    public void The_word_stays_inside_the_band_second_text_adds_one_and_a_click_makes_it_jump()
    {
        var s = TextSettings(); s.BouncingTextSpeed = 10;
        var band = new SKRect(0, 300, 378, 800);
        using var scene = Text(s, visible: band);
        scene.Start();
        Assert.Equal(1, scene.WordCount);
        for (int i = 0; i < 900; i++)
        {
            scene.Tick(1 / 30.0);
            foreach (var w in scene.Placed)
            {
                Assert.InRange(w.X, band.Left, band.Right - w.W + 0.01);
                Assert.InRange(w.Y, band.Top, band.Bottom - w.H + 0.01);
            }
        }
        s.BouncingTextSecondText = true;
        scene.SettingChanged(nameof(AppSettings.BouncingTextSecondText));
        Assert.Equal(2, scene.WordCount);

        var w0 = scene.Placed[0];
        scene.Click(new SKPoint((float)(w0.X + w0.W / 2), (float)(w0.Y + w0.H / 2)));
        Assert.True(scene.Placed[0].Vy < -200);
        // A click beside the word does nothing.
        using var other = Text(TextSettings());
        other.Start();
        var before = other.Placed[0].Vy;
        other.Click(new SKPoint(-50, -50));
        Assert.Equal(before, other.Placed[0].Vy);
    }

    [Fact]
    public void Scrawl_leaves_stamps_at_the_walls_within_its_cap_and_none_when_dark()
    {
        var s = TextSettings(); s.BouncingTextSpeed = 10; s.ScrawlStampCap = 6; s.ScrawlStampSeconds = 60;
        using var lit = Text(s, scrawl: true, visible: new SKRect(0, 0, 378, 400));
        lit.Start();
        Run(lit, 9);
        Assert.InRange(lit.Stamps, 1, 6);
        using var dark = Text(s, scrawl: false, visible: new SKRect(0, 0, 378, 400));
        dark.Start();
        Run(dark, 9);
        Assert.Equal(0, dark.Stamps);
    }

    // ---------------------------------------------------------------- Subliminals

    private static AppSettings SubSettings() => new()
    {
        SubliminalFrequency = 30, SubliminalDuration = 2, SubliminalOpacity = 80,
    };

    private static SubliminalPreviewScene Sub(AppSettings s, MotionLevel level = MotionLevel.Full)
        => Laid(new SubliminalPreviewScene(() => s, () => level, () => false, () => (WordStyle.Classic, WordMotion.Full), new Random(2)));

    [Fact]
    public void Subliminals_wake_and_sleep_and_off_never_animates()
    {
        using var scene = Sub(SubSettings());
        WakesAndSleeps(scene, nameof(AppSettings.SubliminalFrequency));
        using var off = Sub(SubSettings(), MotionLevel.Off);
        OffNeverAnimates(off, nameof(AppSettings.SubTextColor));
        Assert.True(off.CardUp);   // the settled still shows a card
    }

    [Fact]
    public void Subliminal_cards_use_neutral_words_the_frequency_and_the_duration()
    {
        var s = SubSettings();
        using var fast = Sub(s);
        fast.Start();
        Run(fast, 10);
        Assert.InRange(fast.Shown, 8, 11);
        Assert.Contains(fast.LastWord, SubliminalPreviewScene.Words);

        var slow = SubSettings(); slow.SubliminalFrequency = 1;
        using var few = Sub(slow);
        few.Start();
        Run(few, 10);
        Assert.InRange(few.Shown, 2, 3);

        var longer = SubSettings(); longer.SubliminalDuration = 10;
        using var l = Sub(longer);
        l.Start();
        Run(l, 0.5);
        Assert.True(l.LastCardSeconds > fast.LastCardSeconds + 0.1);
        // A click brings the next card now.
        int shown = few.Shown;
        few.Click(new SKPoint(10, 10));
        Run(few, 0.2);
        Assert.Equal(shown + 1, few.Shown);
    }

    // ---------------------------------------------------------------- Bubble Count

    private static BubbleCountPreviewScene Count(AppSettings s, MotionLevel level = MotionLevel.Full, int seed = 4)
        => Laid(new BubbleCountPreviewScene(() => s, () => level, () => false, new Random(seed)));

    [Fact]
    public void Bubble_count_wakes_and_sleeps_and_off_never_animates()
    {
        using var scene = Count(new AppSettings());
        WakesAndSleeps(scene, nameof(AppSettings.BubbleCountDifficulty));
        using var off = Count(new AppSettings(), MotionLevel.Off);
        OffNeverAnimates(off, nameof(AppSettings.BubbleCountFrequency));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 5)]
    [InlineData(2, 8)]
    public void A_round_pops_the_difficultys_count_then_the_pad_types_it(int difficulty, int baseCount)
    {
        var s = new AppSettings { BubbleCountDifficulty = difficulty, BubbleCountFrequency = 10 };
        using var scene = Count(s);
        scene.Start();
        Run(scene, 0.3);
        Assert.Equal(BubbleCountPreviewScene.Phase.Bubbles, scene.Now);
        Assert.InRange(scene.Target, Math.Max(3, baseCount - 1), baseCount + 1);
        int guard = 0;
        while (scene.Now == BubbleCountPreviewScene.Phase.Bubbles && guard++ < 600) scene.Tick(1 / 30.0);
        Assert.Equal(BubbleCountPreviewScene.Phase.Card, scene.Now);
        Assert.Equal(scene.Target, scene.Popped);
        Run(scene, 1.0);
        Assert.True(scene.DigitsLit >= 1);
        Run(scene, 3);
        Assert.Equal(1, scene.Rounds);
    }

    [Fact]
    public void A_click_pops_a_bubble_of_the_round_early()
    {
        var s = new AppSettings { BubbleCountDifficulty = 2 };
        using var scene = Count(s);
        scene.Start();
        Run(scene, 0.5);
        var item = scene.Items.First();
        int popped = scene.Popped;
        scene.Click(new SKPoint((float)item.CenterXPx, (float)item.CenterYPx));
        Assert.Equal(popped + 1, scene.Popped);
        scene.Click(new SKPoint(-40, -40));
        Assert.Equal(popped + 1, scene.Popped);
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
                var bs = BubbleSettings(); bs.BubblesSize = 120;
                Shoot("bubbles", new BubblesPreviewScene(() => bs, () => MotionLevel.Full, () => false, _ => false, () => true, new Random(8)), 6.2, dir);

                var ts = TextSettings(); ts.BouncingTextSecondText = true; ts.BouncingTextOutline = true; ts.BouncingTextSize = 140;
                Shoot("bouncingtext", new BouncingTextPreviewScene(() => ts, () => MotionLevel.Full, () => false, () => true, new Random(6)), 7.0, dir);

                var ss = SubSettings(); ss.SubliminalDuration = 10; ss.SubliminalFrequency = 1;
                Shoot("subliminal", new SubliminalPreviewScene(() => ss, () => MotionLevel.Full, () => false, () => (WordStyle.Classic, WordMotion.Full), new Random(1)), 0.4, dir);

                var cs = new AppSettings { BubbleCountDifficulty = 1, BubbleCountStrictLock = true };
                var count = new BubbleCountPreviewScene(() => cs, () => MotionLevel.Full, () => false, new Random(4));
                Shoot("bubblecount", count, 0, dir, () =>
                {
                    int g = 0;
                    while (count.Now != BubbleCountPreviewScene.Phase.Card && g++ < 900) count.Tick(1 / 30.0);
                    for (int i = 0; i < 18; i++) count.Tick(1 / 30.0);
                });
            }
            finally { EffectPreview.DesktopScaleOverride = null; }
        });
    }

    private static void Shoot(string page, IPreviewScene scene, double seconds, string? dir, Action? advance = null)
    {
        var empty = new EffectPreview { Width = 400, Height = 1100 };
        Layout(empty);
        var wall = Shot(empty, 144);

        var preview = new EffectPreview { Width = 400, Height = 1100 };
        preview.UseScene(scene);
        preview.RasterScale = 0.75;
        Layout(preview);
        preview.Driver!.Open();
        if (advance != null) advance(); else Run(scene, seconds);
        preview.Repaint();
        var shot = Shot(preview, 144);
        if (dir is { Length: > 0 }) { Directory.CreateDirectory(dir); Save(shot, Path.Combine(dir, page + "-preview.png")); }
        Assert.True(Differs(wall, shot) > 2000, $"{page}: the scene drew almost nothing over the desktop");
        scene.Dispose();
    }

    private static int Differs(BitmapSource a, BitmapSource b)
    {
        int w = a.PixelWidth, h = a.PixelHeight, stride = w * 4;
        var pa = new byte[stride * h]; var pb = new byte[stride * h];
        a.CopyPixels(pa, stride, 0); b.CopyPixels(pb, stride, 0);
        int n = 0;
        for (int i = 0; i < pa.Length; i += 4)
            if (Math.Abs(pa[i] - pb[i]) + Math.Abs(pa[i + 1] - pb[i + 1]) + Math.Abs(pa[i + 2] - pb[i + 2]) > 30) n++;
        return n;
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

    private static void Save(BitmapSource b, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(b));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
