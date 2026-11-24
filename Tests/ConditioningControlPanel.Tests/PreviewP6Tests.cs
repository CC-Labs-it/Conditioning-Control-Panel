using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
/// Live Preview lane P6 (2026-11-24): the Voice tab (She's Listening). The scene is a drawn stand-in that
/// opens no device; these run it on an injected clock: a watched setting wakes it, it sleeps after the
/// wake window, the master switch off hears nothing, the wake chip comes before any command word, a click
/// hears one word, Motion Off never animates. With env CCP_ROWS_DUMP=&lt;dir&gt; the render tests save
/// voice-preview.png (400 x 520) and voice.png (the whole tab at 1200, wide enough for the side card).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PreviewP6Tests
{
    private static readonly SKRect Screen = new(0, 0, 374, 494);

    private static AppSettings Armed(bool wake = true, bool ptt = false) => new()
    {
        MicConsentGiven = true, SpeechWakeWordEnabled = wake, SpeechPushToTalkEnabled = ptt,
    };

    private static VoicePreviewScene Make(AppSettings s, MotionLevel level = MotionLevel.Full, int seed = 11)
    {
        var scene = new VoicePreviewScene(() => s, () => level, () => false, new Random(seed));
        scene.Layout(Screen, Screen);
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

    [Fact]
    public void A_watched_setting_wakes_it_and_it_sleeps_after_the_wake_window()
    {
        double now = 50;
        using var scene = Make(Armed());
        var d = new PreviewDriver(scene, () => now);
        Assert.False(d.WantsClock);
        Assert.False(d.SettingChanged(nameof(AppSettings.SubliminalFrequency)));
        Assert.False(d.WantsClock);

        foreach (var name in new[] { nameof(AppSettings.SpeechWakeWordEnabled), nameof(AppSettings.SpeechPushToTalkEnabled),
                     nameof(AppSettings.SpeechLoudnessThreshold), nameof(AppSettings.SpeechMatchThreshold),
                     nameof(AppSettings.SpeechHeadphonesMode), nameof(AppSettings.MicConsentGiven) })
            Assert.True(d.Watches(name), name);

        Assert.True(d.SettingChanged(nameof(AppSettings.SpeechMatchThreshold)));
        Assert.True(d.WantsClock);
        Assert.InRange(RunFrames(d, s => now += s, PreviewDriver.WakeSeconds + 1), 280, 310);
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 4));
        Assert.True(scene.WakesHeard + scene.WordsHeard > 0, "something was heard while awake");

        Assert.True(d.Click(new SKPoint(100, 300)));
        Assert.True(d.WantsClock);
    }

    [Fact]
    public void With_the_master_switch_off_no_word_is_ever_heard()
    {
        // Off three ways: no input mode, no consent, both modes on but consent revoked.
        foreach (var s in new[] { Armed(wake: false, ptt: false), new AppSettings { SpeechWakeWordEnabled = true },
                     new AppSettings { SpeechPushToTalkEnabled = true, SpeechWakeWordEnabled = true } })
        {
            using var scene = Make(s);
            scene.Start();
            for (int i = 0; i < 6; i++) { scene.Click(new SKPoint(150, 200)); Run(scene, 3); }
            Assert.Equal(0, scene.WordsHeard);
            Assert.Equal(0, scene.WakesHeard);
            Assert.Empty(scene.Log);
            Assert.False(scene.Busy);
        }
    }

    [Fact]
    public void With_the_wake_word_on_no_command_lights_before_the_wake_chip()
    {
        var s = Armed(wake: true);
        s.SpeechWakeMatchThreshold = 0.9;   // strict: plenty of wakes are not taken
        using var scene = Make(s);
        scene.Start();
        Run(scene, 120);
        Assert.Contains("wake+", scene.Log);
        Assert.Contains("wake-", scene.Log);
        Assert.Contains(scene.Log, e => e.StartsWith("word+:", StringComparison.Ordinal));
        string? last = null;
        foreach (var e in scene.Log)
        {
            if (e.StartsWith("word", StringComparison.Ordinal))
                Assert.True(last == "wake+", $"a command word ({e}) came after '{last}', not a lit wake chip");
            last = e;
        }

        // Without the wake word the words come with no wake chip at all.
        using var bare = Make(Armed(wake: false, ptt: true));
        bare.Start();
        Run(bare, 30);
        Assert.DoesNotContain(bare.Log, e => e.StartsWith("wake", StringComparison.Ordinal));
        Assert.True(bare.WordsHeard > 0);
    }

    [Fact]
    public void A_click_hears_one_word_now()
    {
        using var scene = Make(Armed(wake: false, ptt: true));
        Run(scene, 0.2);          // before the first word is due on its own
        Assert.Equal(0, scene.WordsHeard);
        scene.Click(new SKPoint(120, 300));
        Assert.True(scene.Busy);
        Run(scene, 1.4);
        Assert.Equal(1, scene.WordsHeard);
        // A second click while that word plays does not stack another one.
        scene.Click(new SKPoint(120, 300));
        Run(scene, 0.3);
        Assert.Equal(1, scene.WordsHeard);
    }

    [Fact]
    public void Precision_and_sensitivity_change_how_often_a_word_is_taken()
    {
        int Taken(double match, double loud)
        {
            var s = Armed(wake: false, ptt: true);
            s.SpeechMatchThreshold = match;
            s.SpeechLoudnessThreshold = loud;
            using var scene = Make(s);
            scene.Start();
            Run(scene, 200);
            return scene.WordsTaken;
        }
        // Loose matching takes more words than strict matching.
        Assert.True(Taken(0.2, 0.010) > Taken(1.0, 0.010) + 5);
        // A deaf gate (lowest sensitivity) hears fewer words than the default.
        Assert.True(Taken(0.62, 0.045) < Taken(0.62, 0.010));
        Assert.True(VoicePreviewScene.Need(new AppSettings { SpeechLoudnessThreshold = 0.045 })
                    > VoicePreviewScene.Need(new AppSettings { SpeechLoudnessThreshold = 0.004 }));
    }

    [Fact]
    public void Off_motion_never_animates_and_held_never_moves()
    {
        double now = 0;
        using var scene = Make(Armed(), MotionLevel.Off);
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
    public void The_id_voice_finds_the_class()
    {
        var type = typeof(FlashPreviewScene).Assembly.GetType(typeof(FlashPreviewScene).Namespace + ".voicePreviewScene", false, true);
        Assert.Equal(typeof(VoicePreviewScene), type);
        using var scene = (IPreviewScene)Activator.CreateInstance(type!, nonPublic: true)!;
        Assert.NotEmpty(scene.Settings);
    }

    [Fact]
    public void The_scene_touches_no_audio_and_hears_only_neutral_words()
    {
        var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Controls", "Preview", "Scenes", "VoicePreviewScene.cs");
        var code = File.ReadAllText(path);
        foreach (var banned in new[] { "System.Speech", "SpeechService", "SpeechRecogn", "Recognizer", "Recogniser", "Vosk",
                     "Sherpa", "KeywordSpot", "NAudio", "WaveIn", "Wasapi", "MMDevice", "Microphone", "AudioDevice",
                     "App.Speech", "App.Autonomy", "MediaPlayer", "SoundPlayer", "LibVLC", "PlaySound" })
            Assert.False(code.Contains(banned, StringComparison.OrdinalIgnoreCase), $"VoicePreviewScene.cs names '{banned}'");

        // The panic words and the real wake word never appear among the words it hears.
        foreach (var w in VoicePreviewScene.Words)
        {
            var low = w.ToLowerInvariant();
            Assert.DoesNotContain(low, new[] { "red", "safe word", "safeword", "stop everything", "make it stop", "all stop", "stop", "hey bambi", "bambi" });
            Assert.False(Regex.IsMatch(low, @"\bred\b|safe|stop|bambi"), w);
        }
        Assert.Equal(new[] { "RELAX", "DEEPER", "FOCUS", "PAUSE" }, VoicePreviewScene.Words);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null);
        return dir!.FullName;
    }

    // ---------------------------------------------------------------- renders

    [Fact]
    [Trait("Category", "Render")]
    public void The_preview_renders_a_lit_word_and_dumps_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        EffectPreview.DesktopScaleOverride = 1.5;
        try
        {
            var dir = Environment.GetEnvironmentVariable("CCP_ROWS_DUMP");
            var empty = new EffectPreview { Width = 400, Height = 520 };
            Layout(empty);
            var emptyShot = Shot(empty, 144);

            var s = Armed(wake: true);
            s.SpeechWakeMatchThreshold = 0.3; s.SpeechWakeThreshold = 0.1; s.SpeechMatchThreshold = 0.3;
            var scene = new VoicePreviewScene(() => s, () => MotionLevel.Full, () => false, new Random(11));
            var busy = new EffectPreview { Width = 400, Height = 520 };
            busy.UseScene(scene);
            busy.RasterScale = 0.75;
            Layout(busy);
            busy.Driver!.Open();
            // Play until a command word has just lit, then a little more so its reaction shows.
            for (int i = 0; i < 30 * 30 && !scene.Log.Any(e => e.StartsWith("word+", StringComparison.Ordinal)); i++)
                scene.Tick(1 / 30.0);
            Assert.Contains(scene.Log, e => e.StartsWith("word+", StringComparison.Ordinal));
            Run(scene, 0.12);   // the chip has just lit, the wave is still up, the reaction begins
            busy.Repaint();
            var shot = Shot(busy, 144);
            if (dir is { Length: > 0 }) { Directory.CreateDirectory(dir); Save(shot, Path.Combine(dir, "voice-preview.png")); }
            var probe = scene.PillCenter;
            int px = (int)((probe.X + PreviewDesktop.Bezel) * 1.5), py = (int)((probe.Y + PreviewDesktop.Bezel) * 1.5);
            Assert.NotEqual(Pixel(emptyShot, px, py), Pixel(shot, px, py));
            scene.Dispose();

            // Motion Off: one composed still with a lit word.
            var still = new VoicePreviewScene(() => s, () => MotionLevel.Off, () => false, new Random(11));
            var off = new EffectPreview { Width = 400, Height = 520 };
            off.UseScene(still);
            Layout(off);
            off.Repaint();
            if (dir is { Length: > 0 }) Save(Shot(off, 144), Path.Combine(dir, "voice-preview-still.png"));
            still.Dispose();
        }
        finally { EffectPreview.DesktopScaleOverride = null; }
        RowsRender.AssertSilent("voice preview");
    });

    /// <summary>
    /// The whole tab off screen. Safe to build: its constructor only runs InitializeComponent and hooks
    /// Loaded (mod art), and every handler goes through Window.GetWindow(this) is MainWindow, which is null
    /// here, so nothing arms, enumerates a device or loads a model. Wide enough (1200) that the side card
    /// is not collapsed (AdaptiveSideArt.CollapseBelow is 1000 on this tab). With CCP_P6_NAME set the
    /// still takes that name (the BEFORE still was saved as voice-before).
    /// </summary>
    [Fact]
    [Trait("Category", "Render")]
    public void The_tab_renders_silently_with_the_preview_in_its_card() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SheListeningTabView();
        var host = RowsRender.Host(page, width: 1200);
        var pv = RowsRender.Descendants(page).OfType<EffectPreview>().Single();
        Assert.IsType<VoicePreviewScene>(pv.Driver!.Scene);
        Assert.InRange(pv.ActualHeight, 500, 520);
        RowsRender.Dump(host, Environment.GetEnvironmentVariable("CCP_P6_NAME") is { Length: > 0 } n ? n : "voice");
        RowsRender.AssertSilent("voice tab");
    });

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
