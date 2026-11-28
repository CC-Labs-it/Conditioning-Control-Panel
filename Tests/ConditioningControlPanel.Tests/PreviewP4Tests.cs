using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
/// Live Preview lane P4 (2026-11-24): the toy page's pulse monitor. On an injected clock: a watched
/// setting wakes it, it sleeps after the wake window, the trace never crosses the Max power line, a click
/// is one test pulse, Motion Off never animates, the show's rows drive the kicks, Delay moves the audio
/// kick late, "Stand aside for" flattens the floor. A source scan proves the scene names nothing that could
/// reach a toy. With env CCP_ROWS_DUMP=&lt;dir&gt; the render test saves haptics-preview.png (the card at its
/// real size) and haptics.png (the whole tab at 900 px).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PreviewP4Tests
{
    private static readonly SKRect Screen = new(0, 0, 300, 520);

    private static HapticsPreviewScene Make(AppSettings s, MotionLevel level = MotionLevel.Full, int seed = 7)
    {
        var scene = new HapticsPreviewScene(() => s, () => level, () => false, new Random(seed));
        scene.Layout(Screen, Screen);
        return scene;
    }

    /// <summary>Settings with the show's rows set (on/off, strength) and nothing else changed.</summary>
    private static AppSettings With(bool rows = true, double amp = 0.5)
    {
        var s = new AppSettings();
        foreach (var key in new[] { "FlashDecay", "FlashClick", "SubliminalTrigger", "BubblePop" })
            s.Haptics.V2.Events[key] = new HapticEventRule { Enabled = rows, Intensity = amp };
        return s;
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
    public void A_watched_setting_wakes_it_and_it_sleeps_after_the_wake_window()
    {
        double now = 50;
        using var scene = Make(With());
        var d = new PreviewDriver(scene, () => now);
        Assert.False(d.WantsClock);
        Assert.False(d.SettingChanged(nameof(AppSettings.SubliminalFrequency)));
        Assert.False(d.WantsClock);

        foreach (var name in new[] { nameof(HapticSettings.GlobalIntensity), nameof(HapticSettings.V2),
                     nameof(AudioSyncSettings.ManualLatencyOffsetMs), nameof(AudioSyncSettings.BassWeight), nameof(AppSettings.Haptics) })
            Assert.Contains(name, scene.Settings);

        Assert.True(d.SettingChanged(nameof(HapticSettings.GlobalIntensity)));
        Assert.True(d.WantsClock);
        Assert.InRange(RunFrames(d, s => now += s, PreviewDriver.WakeSeconds + 1), 280, 310);
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 4));

        Assert.True(d.Click(new SKPoint(150, 300)));
        Assert.True(d.WantsClock);
    }

    [Fact]
    public void The_trace_never_crosses_the_max_power_line()
    {
        var s = With(amp: 1.0);
        s.Haptics.GlobalIntensity = 1.0;
        s.Haptics.V2.MasterCap = 0.35;
        s.Haptics.V2.Temperament = "cruel";
        s.Haptics.AudioSync.Enabled = true;
        s.Haptics.AudioSync.Sensitivity = 3.0;
        s.Haptics.LuminanceSyncEnabled = true;
        s.Haptics.LuminanceSyncIntensity = 1.0;
        using var scene = Make(s);
        scene.Start();
        double worst = 0;
        int step = 0;
        Run(scene, 8, () =>
        {
            worst = Math.Max(worst, scene.Level);
            if (++step % 9 == 0) scene.Click(new SKPoint(5, 5));
        });
        Assert.Equal(0.35, scene.Cap, 3);
        Assert.True(worst <= scene.Cap + 1e-9, $"level {worst} over the cap {scene.Cap}");
        Assert.True(scene.TraceMax <= scene.Cap + 1e-6, $"trace {scene.TraceMax} over the cap {scene.Cap}");
        Assert.True(scene.TraceMax > scene.Cap - 0.01, "the ceiling was reached and held");

        // The shipped default ceiling when the file has none worth reading.
        s.Haptics.V2.MasterCap = double.NaN;
        Assert.Equal(HapticsPreviewScene.DefaultCap, scene.Cap, 3);
    }

    [Fact]
    public void A_click_is_one_test_pulse_and_the_trace_answers_it()
    {
        var s = With(rows: false);
        using var scene = Make(s);
        Run(scene, 2);
        Assert.Equal(0, scene.PulsesSent);
        Assert.Equal(0, scene.TraceMax);

        scene.Click(new SKPoint(scene.Capsule.X, scene.Capsule.Y));
        Assert.Equal(1, scene.PulsesSent);
        Assert.Equal(1, scene.TestPulses);
        double peak = 0;
        Run(scene, 0.3, () => peak = Math.Max(peak, scene.Level));
        Assert.True(peak > 0.3, $"peak {peak}");
        Run(scene, 1.5);
        Assert.Equal(0, scene.Level, 3);
        Assert.Equal(1, scene.PulsesSent);
    }

    [Fact]
    public void Off_motion_never_animates_held_never_moves_and_the_still_is_settled()
    {
        double now = 0;
        using var scene = Make(With(), MotionLevel.Off);
        var d = new PreviewDriver(scene, () => now) { Pace = PreviewGate.Pace(MotionLevel.Off, false, true, true) };
        d.Open();
        d.SettingChanged(nameof(HapticSettings.GlobalIntensity));
        d.Click(new SKPoint(5, 5));
        Assert.False(d.WantsClock);
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
        Assert.Equal(0, d.Ticks);
        // The still: the settle filled a whole trace, and nothing shakes.
        Assert.True(scene.TraceMax > 0);
        Assert.Equal(0, scene.Jitter);

        d.Pace = PreviewGate.Pace(MotionLevel.Full, panelQuiet: true, allowAmbientLoops: false, tierAllowsMotion: true);
        Assert.False(d.Click(new SKPoint(1, 1)));
        d.SettingChanged(nameof(HapticSettings.GlobalIntensity));
        Assert.Equal(0, RunFrames(d, s => now += s, 3));
    }

    [Fact]
    public void The_id_haptics_resolves_by_its_class_name()
    {
        var type = typeof(FlashPreviewScene).Assembly.GetType(typeof(FlashPreviewScene).Namespace + ".hapticsPreviewScene", false, true);
        Assert.Equal(typeof(HapticsPreviewScene), type);
        using var scene = (IPreviewScene)Activator.CreateInstance(type!, nonPublic: true)!;
        Assert.NotEmpty(scene.Settings);
    }

    [Fact]
    public void The_show_kicks_only_through_rows_that_are_on_and_the_master_switch_flattens_it()
    {
        using var off = Make(With(rows: false));
        off.Start();
        Run(off, 6);
        Assert.Equal(0, off.ItemsShown);
        Assert.Equal(0, off.PulsesSent);

        var s = With(amp: 0.8);
        using var on = Make(s);
        on.Start();
        Run(on, 6);
        Assert.InRange(on.ItemsShown, 3, 6);   // one every 1.3 s
        Assert.True(on.PulsesSent >= 3);
        Assert.True(on.TraceMax > 0.2);

        s.Haptics.Enabled = false;
        Run(on, HapticsPreviewScene.TraceSeconds + 0.5);
        Assert.True(on.ItemsShown >= 6);       // the effects still run on screen
        Assert.Equal(0, on.TraceMax);          // but the toy feels nothing
    }

    [Fact]
    public void Delay_moves_the_audio_kick_late_and_power_sizes_it()
    {
        int PeakFrame(int delayMs, double power, out double peak)
        {
            var s = With(rows: false);
            s.Haptics.AudioSync.Enabled = true;
            s.Haptics.AudioSync.Smoothing = 0;
            s.Haptics.AudioSync.ManualLatencyOffsetMs = delayMs;
            s.Haptics.AudioSync.LiveIntensity = power;
            using var scene = Make(s);
            var levels = new List<double>();
            Run(scene, 4 * HapticsPreviewScene.BeatSeconds, () => levels.Add(scene.Level));
            // One beat (24 frames at 30 fps) from just before the fourth beat, at 2.4 s = frame 71.
            int from = 69, to = Math.Min(levels.Count, from + 24);
            int best = from;
            for (int i = from; i < to; i++) if (levels[i] > levels[best]) best = i;
            peak = levels[best];
            return best - from;
        }
        int early = PeakFrame(0, 1.0, out var full);
        int late = PeakFrame(400, 1.0, out _);
        PeakFrame(0, 0.4, out var weak);
        Assert.InRange(late - early, 10, 14);  // 400 ms = 12 frames
        Assert.True(weak < full * 0.6, $"power 40% gives {weak} against {full}");
        Assert.True(full > 0.2);
    }

    [Fact]
    public void Stand_aside_flattens_the_floor_after_a_test_pulse_only_with_toy_input_on()
    {
        double FloorAfterPress(bool toyInput)
        {
            var s = With(rows: false);
            s.Haptics.AudioSync.Enabled = true;
            s.Haptics.AudioSync.Smoothing = 0;
            s.Haptics.ToyInputEnabled = toyInput;
            s.Haptics.UserOverrideCooldownSec = 120;
            using var scene = Make(s);
            Run(scene, 1);
            scene.Click(new SKPoint(5, 5));
            Run(scene, 0.7);   // the test pulse is gone by now
            double most = 0;
            Run(scene, 1.6, () => most = Math.Max(most, scene.Level));
            Assert.Equal(toyInput, scene.StandingAside);
            return most;
        }
        Assert.Equal(0, FloorAfterPress(true), 6);
        Assert.True(FloorAfterPress(false) > 0.1);
        Assert.Equal(0.8, HapticsPreviewScene.StandAside(5), 3);
        Assert.Equal(3.5, HapticsPreviewScene.StandAside(120), 3);
    }

    /// <summary>The scene names nothing that could reach a toy: no service, connection or device type.</summary>
    [Fact]
    public void The_scene_names_no_toy_service_connection_or_device()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Controls", "Preview")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var file = Path.Combine(dir!.FullName, "ConditioningControlPanel", "Controls", "Preview", "Scenes", "HapticsPreviewScene.cs");
        var text = File.ReadAllText(file);
        string[] banned =
        {
            "App.Haptics", "Services.Haptics", "HapticService", "IHapticProvider", "HapticMixer", "HapticTemperament",
            "Lovense", "Buttplug", "Intiface", "Provider", "Device", "Director", "ToyInputService", "FunScriptService",
            "LockdownDose", "SetLayer", "SendPulse", "Vibrate", "Connect", "HttpClient", "WebSocket", "Socket",
            "Settings.Save", "Settings?.Save", "NAudio", "WaveOut", "PlaySound",
        };
        var hits = banned.Where(w => text.Contains(w, StringComparison.Ordinal)).ToList();
        Assert.True(hits.Count == 0, "HapticsPreviewScene.cs names " + string.Join(", ", hits));
        // Settings are only read: no assignment into the toy settings anywhere in the scene.
        Assert.DoesNotContain(".Haptics.", text);
        Assert.DoesNotContain("Events[", text);
    }

    // ---------------------------------------------------------------- renders

    [Fact]
    [Trait("Category", "Render")]
    public void The_preview_and_the_tab_render_and_dump_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_ROWS_DUMP");

        // The whole tab, built as RowsR6aTests does (plain construction, nothing invoked). At 900 px the tab's
        // side column folds away (AdaptiveSideArt CollapseBelow 1000, as before the preview), so a second still
        // at 1200 px shows the card.
        static (Grid Host, EffectPreview Card) Tab(double width)
        {
            var page = new HapticsTabView();
            var host = RowsRender.Host(page, width, show: new[] { "VideoHapticSyncSliders", "HapticAudioAdvanced" }, before: p =>
            {
                foreach (var e in RowsRender.Descendants(p).OfType<Expander>().ToList()) e.IsExpanded = true;
            });
            return (host, RowsRender.Descendants(page).OfType<EffectPreview>().Single());
        }
        var narrow = Tab(900);
        Assert.NotEqual(Visibility.Visible, ((FrameworkElement)narrow.Card.Parent).Visibility);   // folds under 1000 px
        RowsRender.Dump(narrow.Host, "haptics");
        var wide = Tab(1200);
        var card = wide.Card;
        var side = (FrameworkElement)card.Parent;
        Assert.Equal(Visibility.Visible, side.Visibility);
        Assert.True(card.ActualWidth > 150 && card.ActualHeight > 150, $"card {card.ActualWidth} x {card.ActualHeight}");
        Assert.True(card.ActualHeight <= 560.5);
        Assert.IsType<HapticsPreviewScene>(card.Driver!.Scene);
        RowsRender.Dump(wide.Host, "haptics-1200");
        RowsRender.AssertSilent("haptics tab");

        // The card alone at its real size, a lively moment: show on, audio sync on, a test pulse in flight.
        EffectPreview.DesktopScaleOverride = 1.5;
        try
        {
            var s = With(amp: 0.7);
            s.Haptics.AudioSync.Enabled = true;
            s.Haptics.V2.MasterCap = 0.7;
            var scene = new HapticsPreviewScene(() => s, () => MotionLevel.Full, () => false, new Random(3));
            var pv = new EffectPreview { Width = Math.Round(card.ActualWidth), Height = Math.Round(card.ActualHeight) };
            pv.UseScene(scene);
            pv.RasterScale = 0.75;
            Lay(pv);
            var bare = Shot(new EffectPreview { Width = pv.Width, Height = pv.Height }, 144, lay: true);
            pv.Driver!.Open();
            Run(scene, 3.25);
            scene.Click(scene.Capsule);
            Run(scene, 0.12);
            pv.Repaint();
            var shot = Shot(pv, 144);
            if (dir is { Length: > 0 }) { Directory.CreateDirectory(dir); Save(shot, Path.Combine(dir, "haptics-preview.png")); }
            var m = scene.Monitor;
            int px = (int)((m.MidX + PreviewDesktop.Bezel) * 1.5), py = (int)((m.Bottom - 20 + PreviewDesktop.Bezel) * 1.5);
            Assert.NotEqual(Pixel(bare, px, py), Pixel(shot, px, py));
            scene.Dispose();
        }
        finally { EffectPreview.DesktopScaleOverride = null; }
    });

    private static void Lay(FrameworkElement e)
    {
        e.Measure(new Size(e.Width, e.Height));
        e.Arrange(new Rect(0, 0, e.Width, e.Height));
        e.UpdateLayout();
    }

    private static BitmapSource Shot(FrameworkElement e, double dpi, bool lay = false)
    {
        if (lay) Lay(e);
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
