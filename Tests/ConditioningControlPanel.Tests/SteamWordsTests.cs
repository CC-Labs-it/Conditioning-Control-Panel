using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The words in the Steam fog (owner, 2026-11-15): a word shows where the fog was wiped and came
/// back, holds a few seconds, fades, and the place must be wiped again to speak again; the words
/// are drawn at the monitor's own resolution; one switch turns them off.
/// </summary>
public class SteamWordsTests
{
    private const int W = 1920, H = 1080;
    private const double Dt = 1 / 30.0;
    private static readonly string[] Pool = { "sink", "soft", "drift", "blank", "deeper", "bimbo doll" };

    private static SteamField Fogged()
    {
        var f = new SteamField(W, H);
        f.Settle(1.5, 1);
        return f;
    }

    /// <summary>A finger across the top-left place (slot 0).</summary>
    private static void WipeTopLeft(SteamField f)
    {
        for (int pass = 0; pass < 3; pass++)
            for (double x = 110; x <= 660; x += 14) f.Wipe(x, 146 + pass * 22 - 22, 60, Dt, mark: true);
    }

    private static void Run(SteamField f, SteamWords w, double seconds, bool gentle = false, bool open = true, Action? each = null)
    {
        for (double t = 0; t < seconds; t += Dt)
        {
            f.Step(Dt, 1.5, 1);
            w.Step(f, Dt, gentle, open);
            each?.Invoke();
        }
    }

    private static SteamWords Words()
    {
        var w = new SteamWords(seed: 7);
        w.SetPool(Pool);
        return w;
    }

    [Fact]
    public void Untouched_fog_shows_no_word()
    {
        var f = Fogged(); var w = Words();
        Run(f, w, 20);
        Assert.False(w.Any);
        Assert.All(w.Slots, s => Assert.Equal(SteamWordPhase.Hidden, s.Phase));
    }

    [Fact]
    public void A_wiped_place_speaks_once_the_fog_is_back_holds_and_then_fades_out()
    {
        var f = Fogged(); var w = Words();
        WipeTopLeft(f);
        var slot = w.Slots[0];
        w.Step(f, Dt, false);
        Assert.Equal(SteamWordPhase.Hidden, slot.Phase);   // the finger has only just left: clear glass, no word yet

        double shown = 0, peak = 0, last = 0, biggestStep = 0;
        bool held = false;
        Run(f, w, 30, each: () =>
        {
            if (slot.Alpha > 0.002) shown += Dt;
            if (slot.Phase == SteamWordPhase.Hold) held = true;
            peak = Math.Max(peak, slot.Alpha);
            biggestStep = Math.Max(biggestStep, Math.Abs(slot.Alpha - last));
            last = slot.Alpha;
        });
        Assert.True(held, "the word held");
        Assert.Equal(1, peak, 3);
        Assert.Equal(SteamWordPhase.Hidden, slot.Phase);
        Assert.Equal(0, slot.Alpha);
        Assert.False(w.Any);
        Assert.Equal(1, slot.Serial);
        // On screen for its in, its hold and its out, and no longer.
        double whole = SteamWords.InSeconds + SteamWords.HoldSeconds + SteamWords.OutSeconds;
        Assert.InRange(shown, SteamWords.HoldSeconds, whole + 0.1);
        Assert.True(biggestStep < 0.08, $"fades only, biggest step {biggestStep:0.000}");
        // The place is forgotten: fog alone does not bring the word back.
        Assert.Equal(0, SteamWords.Measure(f, slot).Wiped);
        Run(f, w, 30);
        Assert.Equal(1, slot.Serial);
    }

    [Fact]
    public void A_faded_place_speaks_again_when_it_is_wiped_again_with_another_word()
    {
        var f = Fogged(); var w = Words();
        var slot = w.Slots[0];
        var seen = new List<string>();
        for (int round = 0; round < 4; round++)
        {
            WipeTopLeft(f);
            Run(f, w, 25);
            Assert.Equal(round + 1, slot.Serial);
            Assert.Equal(SteamWordPhase.Hidden, slot.Phase);
            seen.Add(slot.Text);
        }
        for (int i = 1; i < seen.Count; i++) Assert.NotEqual(seen[i - 1], seen[i]);
    }

    [Fact]
    public void Two_places_never_show_the_same_word_while_the_pool_allows()
    {
        var f = Fogged(); var w = Words();
        for (int gy = 0; gy < f.H; gy++) for (int gx = 0; gx < f.W; gx++) f.Mem[gy * f.W + gx] = 1;
        Run(f, w, 3);
        var showing = w.Slots.Where(s => !s.IsLine && s.Phase != SteamWordPhase.Hidden).Select(s => s.Text).ToList();
        Assert.Equal(6, showing.Count);
        Assert.Equal(6, showing.Distinct().Count());
        Assert.Equal(SteamWordPhase.Hidden, w.Slots.Single(s => s.IsLine).Phase);   // no line was given
        w.Line = "let it all go soft";
        Run(f, w, 40);
        Assert.Equal(1, w.Slots.Single(s => s.IsLine).Serial);
    }

    [Fact]
    public void Switched_off_no_place_wakes_and_a_word_on_screen_fades_instead_of_cutting()
    {
        var f = Fogged(); var w = Words();
        WipeTopLeft(f);
        Run(f, w, 30, open: false);
        Assert.False(w.Any);
        Assert.Equal(0, w.Slots[0].Serial);

        // On, a word comes up; off mid-hold, it leaves over the out and nothing replaces it.
        var slot = w.Slots[0];
        for (int i = 0; i < 900 && slot.Phase != SteamWordPhase.Hold; i++) { f.Step(Dt, 1.5, 1); w.Step(f, Dt, false); }
        Assert.Equal(SteamWordPhase.Hold, slot.Phase);
        w.Step(f, Dt, false, open: false);
        Assert.Equal(SteamWordPhase.Out, slot.Phase);
        Assert.True(slot.Alpha > 0.9, "no cut");
        double last = slot.Alpha;
        Run(f, w, SteamWords.OutSeconds + 0.2, open: false, each: () => { Assert.True(slot.Alpha <= last + 1e-9); last = slot.Alpha; });
        Assert.False(w.Any);
        WipeTopLeft(f);
        Run(f, w, 30, open: false);
        Assert.Equal(1, slot.Serial);
    }

    [Fact]
    public void Gentle_visuals_only_lengthen_the_in_and_the_out()
    {
        Assert.True(SteamWords.InFor(false) >= 0.3, "rise floor");
        Assert.True(SteamWords.InFor(true) > SteamWords.InFor(false));
        Assert.True(SteamWords.OutFor(true) > SteamWords.OutFor(false));
        var f = Fogged(); var w = Words();
        WipeTopLeft(f);
        var slot = w.Slots[0];
        double last = 0, biggest = 0;
        Run(f, w, 40, gentle: true, each: () => { biggest = Math.Max(biggest, Math.Abs(slot.Alpha - last)); last = slot.Alpha; });
        Assert.Equal(1, slot.Serial);
        Assert.Equal(SteamWordPhase.Hidden, slot.Phase);
        Assert.True(biggest < 0.05, $"biggest step {biggest:0.000}");
    }

    private static SKBitmap Canvas(out SKCanvas canvas)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(22, 26, 38));
        return bmp;
    }

    private static readonly SKColor Pink = new(255, 105, 180);
    private static SKTypeface Face() => SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    [Fact]
    public void A_held_word_is_drawn_inside_its_place_and_laid_out_once()
    {
        var f = Fogged(); var w = Words();
        WipeTopLeft(f);
        var slot = w.Slots[0];
        for (int i = 0; i < 900 && slot.Phase != SteamWordPhase.Hold; i++) { f.Step(Dt, 1.5, 1); w.Step(f, Dt, false); }
        Run(f, w, 2);
        Assert.Equal(SteamWordPhase.Hold, slot.Phase);

        using var bmp = Canvas(out var canvas);
        using var draw = new SteamWordsDraw();
        using var face = Face();
        for (int i = 0; i < 5; i++) draw.Draw(canvas, new SKRect(0, 0, W, H), f, w, Pink, face);
        canvas.Flush();
        Assert.Equal(1, draw.Layouts);
        int lit = 0, litOutside = 0, partial = 0;
        for (int y = 0; y < H; y += 1)
            for (int x = 0; x < W; x += 1)
            {
                var p = bmp.GetPixel(x, y);
                if (p.Red == 22 && p.Green == 26) continue;
                bool inside = x > 40 && x < 740 && y > 20 && y < 290;
                if (inside) lit++; else litOutside++;
                if (p.Red is > 60 and < 215) partial++;
            }
        canvas.Dispose();
        Assert.True(lit > 4000, $"word pixels {lit}");
        Assert.Equal(0, litOutside);
        // Soft edges and a bloom: a good share of the lit pixels are in-between tones, not on/off.
        Assert.True(partial > lit / 5, $"partial {partial} of {lit}");
    }

    [Fact]
    public void Nothing_showing_draws_nothing()
    {
        var f = Fogged(); var w = Words();
        WipeTopLeft(f);
        Run(f, w, 30, open: false);   // the switch is off
        using var bmp = Canvas(out var canvas);
        using var draw = new SteamWordsDraw();
        using var face = Face();
        draw.Draw(canvas, new SKRect(0, 0, W, H), f, w, Pink, face);
        canvas.Flush();
        canvas.Dispose();
        Assert.Equal(0, draw.Layouts);
        var first = bmp.GetPixel(0, 0);
        for (int y = 0; y < H; y += 3)
            for (int x = 0; x < W; x += 3) Assert.Equal(first, bmp.GetPixel(x, y));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    [Fact]
    public void The_switch_is_one_setting_on_by_default_read_by_the_layer_and_shown_in_the_gold_box()
    {
        Assert.True(new Models.AppSettings().SteamWordsInFog);
        var root = Root();
        var layer = File.ReadAllText(Path.Combine(root, "Services", "Compositor", "SteamLayer.cs"));
        Assert.Contains("SteamWordsInFog", layer);
        // The fog drawer gets no words: the old quarter-resolution words never fire.
        Assert.Contains("Array.Empty<string>(), null, Face", layer);
        var xaml = File.ReadAllText(Path.Combine(root, "Features", "PinkFilterFeatureControl.xaml"));
        int box = xaml.IndexOf("<ctl:SuperBox", StringComparison.Ordinal), end = xaml.IndexOf("</ctl:SuperBox>", StringComparison.Ordinal);
        int at = xaml.IndexOf("x:Name=\"ChkSteamWords\"", StringComparison.Ordinal);
        Assert.InRange(at, box, end);
        Assert.Contains("{loc:Str super_steam_words}", xaml);
    }

    [Fact]
    public void The_switch_reads_in_all_ten_languages()
    {
        var files = Directory.GetFiles(Path.Combine(Root(), "..", "CCP.Core", "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Path.GetFileName(file) + " has a BOM");
            var json = JObject.Parse(File.ReadAllText(file));
            foreach (var key in new[] { "super_steam_words", "tooltip_super_steam_words" })
            {
                var text = (string?)json[key];
                Assert.False(string.IsNullOrWhiteSpace(text), $"{Path.GetFileName(file)} {key}");
                Assert.DoesNotContain("!", text);
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
                Assert.False(LocalizationEncodingTests.LooksDoubleEncoded(text!), $"{Path.GetFileName(file)} {key}");
            }
        }
    }

    /// <summary>
    /// Proof in pictures. With CCP_STEAM_DUMP=dir set, renders the life of a word at 1920x1080
    /// (fog settled, a wipe, the fog closing, the word writing in, holding, melting, gone) plus a
    /// 100% crop of the held word. Without the variable it only checks the strip can be drawn.
    /// </summary>
    [Fact]
    public void Dump_the_life_of_a_word()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_STEAM_DUMP");
        bool dump = !string.IsNullOrEmpty(dir);
        if (dump) Directory.CreateDirectory(dir!);
        var f = Fogged();
        var w = new SteamWords(seed: 3);
        w.SetPool(new[] { "bimbo doll" });
        w.Line = "let it all go soft";
        using var fog = new SteamDraw();
        using var draw = new SteamWordsDraw();
        using var face = Face();
        var slot = w.Slots[0];
        double t = 0;
        int n = 0;

        void Shot(string name, bool crop = false)
        {
            using var bmp = Canvas(out var canvas);
            // A desk behind the fog, so the fog and the words are judged over something.
            using (var p = new SKPaint { IsAntialias = true })
            {
                for (int i = 0; i < 9; i++)
                {
                    p.Color = new SKColor((byte)(40 + i * 12), (byte)(60 + i * 9), (byte)(90 + i * 7));
                    canvas.DrawRect(new SKRect(120 + i * 190, 140 + (i % 3) * 230, 280 + i * 190, 300 + (i % 3) * 230), p);
                }
            }
            fog.Draw(canvas, new SKRect(0, 0, W, H), f, Pink, 0.62, Array.Empty<string>(), null, face, t);
            draw.Draw(canvas, new SKRect(0, 0, W, H), f, w, Pink, face);
            canvas.Flush();
            canvas.Dispose();
            if (!dump) return;
            string file = Path.Combine(dir!, $"{n++:00}-{name}.png");
            if (crop)
            {
                using var part = new SKBitmap();
                bmp.ExtractSubset(part, new SKRectI(60, 30, 760, 290));
                using var data = part.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(file, data.ToArray());
            }
            else
            {
                using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(file, data.ToArray());
            }
        }

        void Go(double seconds) { Run(f, w, seconds); t += seconds; }
        bool Until(Func<bool> done)
        {
            for (int i = 0; i < 1800 && !done(); i++) Go(Dt);
            return done();
        }

        Shot("fog-settled");
        WipeTopLeft(f);
        // The whole middle too, for the line.
        f.Remember(W * 0.06, H * 0.38, W * 0.94, H * 0.66);
        Shot("wiped");
        Go(1.2);
        Shot("fog-closing");
        Assert.True(Until(() => slot.Phase == SteamWordPhase.In && slot.Write >= 0.35));
        Shot("writing-in");
        Shot("writing-in-crop", crop: true);
        Assert.True(Until(() => slot.Phase == SteamWordPhase.Hold));
        Go(2.5);
        Shot("holding");
        Shot("holding-crop", crop: true);
        Assert.True(Until(() => slot.Phase == SteamWordPhase.Out && slot.Melt >= 0.45));
        Shot("melting");
        Shot("melting-crop", crop: true);
        Assert.True(Until(() => slot.Phase == SteamWordPhase.Hidden));
        Go(6);
        Shot("gone");
        Assert.False(w.Any);
    }
}
