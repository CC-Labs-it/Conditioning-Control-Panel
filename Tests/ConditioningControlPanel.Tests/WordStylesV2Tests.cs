using System;
using System.IO;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The worn word style on the two V2 surfaces that show words (owner, 2026-11-17): Afterglow's
/// ghosts and the words in the Steam fog wear the style and play its way in; Classic is today's
/// look. Plus the "Also show the normal subliminals" switch. Stills with CCP_WORDS_DUMP=dir.
/// </summary>
public class WordStylesV2Tests
{
    private static readonly WordStyle[] Styles = { WordStyle.Classic, WordStyle.Type, WordStyle.Chalk, WordStyle.Drift, WordStyle.Echo, WordStyle.Marquee };
    private static readonly SKTypeface Face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    // ---- the rule: a life that settles at a given moment ----

    [Theory]
    [InlineData(0.3)]
    [InlineData(1.25)]
    [InlineData(2.8)]
    [InlineData(9)]
    public void A_life_settled_at_a_moment_starts_its_leaving_there(double at)
    {
        double life = WordStyles.LifeSettledAt(at);
        Assert.Equal(at, life - WordStyles.OutSeconds(life), 9);
    }

    [Fact]
    public void Every_style_is_settled_when_its_clock_is_held()
    {
        var f = new WordFrame();
        double life = AfterglowLine.StyleLife, at = AfterglowLine.LandSeconds;
        foreach (var style in Styles)
            foreach (var motion in new[] { WordMotion.Full, WordMotion.Reduced })
                foreach (int n in new[] { 1, 4, 12, 28 })
                {
                    WordStyles.Fill(style, n, at, life, motion, f);
                    for (int i = 0; i < n; i++)
                    {
                        var l = f.Letters[i];
                        Assert.True(l.Alpha == 1 && l.Reveal == 1 && l.Lit == 1 && Math.Abs(l.Dy) < 1e-9, $"{style} {motion} {n} letter {i}");
                    }
                    Assert.Equal(0, f.CursorAlpha);
                }
    }

    [Fact]
    public void A_ghost_plays_its_style_while_it_rises_and_holds_it_once_landed()
    {
        var l = new AfterglowLine();
        Assert.True(l.Add("sink"));
        l.Step(0.2);
        Assert.Equal(0.2, l.StyleClock(0), 9);
        var f = new WordFrame();
        WordStyles.Fill(WordStyle.Type, 4, l.StyleClock(0), AfterglowLine.StyleLife, WordMotion.Full, f);
        Assert.True(f.Letters[3].Alpha < 1);              // still typing as it rises
        l.Step(5);
        Assert.Equal(AfterglowLine.LandSeconds, l.StyleClock(0), 9);
        // The style is settled before the line stops repainting.
        Assert.False(l.Animating);
    }

    [Fact]
    public void A_fog_word_keeps_a_life_clock_from_its_first_frame()
    {
        var (field, words) = FullFog(960, 540);
        words.Step(field, 0.016, gentle: false);
        var slot = words.Slots[0];
        Assert.Equal(SteamWordPhase.In, slot.Phase);
        Assert.Equal(0, slot.Life, 9);
        for (int i = 0; i < 100; i++) words.Step(field, 0.02, false);
        Assert.Equal(2.0, slot.Life, 6);
        Assert.Equal(SteamWords.InSeconds + SteamWords.HoldSeconds + SteamWords.OutSeconds, SteamWords.LifeFor(false), 9);
        Assert.True(SteamWords.LifeFor(true) > SteamWords.LifeFor(false));
    }

    // ---- Classic is today's look; a style changes it ----

    [Fact]
    public void Classic_afterglow_draws_exactly_the_plain_words()
    {
        using var plain = DrawAfterglow(null, WordMotion.Full, AfterglowStage.Rising);
        using var classic = DrawAfterglow(WordStyle.Classic, WordMotion.Full, AfterglowStage.Rising);
        Assert.True(Same(plain, classic));
        using var marquee = DrawAfterglow(WordStyle.Marquee, WordMotion.Full, AfterglowStage.Rising);
        Assert.False(Same(plain, marquee));
    }

    [Fact]
    public void Classic_fog_words_draw_exactly_the_old_write_on()
    {
        using var plain = DrawFog(null, WordMotion.Full, 0.6);
        using var classic = DrawFog(WordStyle.Classic, WordMotion.Full, 0.6);
        Assert.True(Same(plain, classic));
        using var typed = DrawFog(WordStyle.Type, WordMotion.Full, 0.6);
        Assert.False(Same(plain, typed));
    }

    [Fact]
    public void Typed_fog_words_come_in_letter_by_letter()
    {
        long early = InkOf(DrawFog(WordStyle.Type, WordMotion.Full, 0.6));
        long later = InkOf(DrawFog(WordStyle.Type, WordMotion.Full, 0.9));
        long held = InkOf(DrawFog(WordStyle.Type, WordMotion.Full, 3.0));
        Assert.True(early < later && later < held, $"{early} {later} {held}");
        // Gentle / motion off: no typing, the word only rides the fog's own fade.
        long still = InkOf(DrawFog(WordStyle.Type, WordMotion.Still, 0.6));
        Assert.True(still > early, $"{still} {early}");
    }

    [Fact]
    public void A_held_styled_fog_word_is_laid_out_once()
    {
        var (field, words) = FullFog(960, 540);
        using var draw = new SteamWordsDraw();
        using var bmp = new SKBitmap(960, 540);
        using var canvas = new SKCanvas(bmp);
        for (int i = 0; i < 10; i++) words.Step(field, 1 / 30.0, false);
        draw.Draw(canvas, new SKRect(0, 0, 960, 540), field, words, new SKColor(255, 105, 180), Face, WordStyle.Marquee, WordMotion.Full);
        int laid = draw.Layouts;
        Assert.True(laid > 0);
        for (int i = 0; i < 60; i++)
        {
            words.Step(field, 1 / 30.0, false);
            draw.Draw(canvas, new SKRect(0, 0, 960, 540), field, words, new SKColor(255, 105, 180), Face, WordStyle.Marquee, WordMotion.Full);
        }
        Assert.Equal(laid, draw.Layouts);
        // Taking a different style off the shelf lays the words out again in it.
        draw.Draw(canvas, new SKRect(0, 0, 960, 540), field, words, new SKColor(255, 105, 180), Face, WordStyle.Chalk, WordMotion.Full);
        Assert.True(draw.Layouts > laid);
    }

    [Fact]
    public void The_layers_hand_their_words_the_worn_style()
    {
        var root = Root();
        var steam = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "Services", "Compositor", "SteamLayer.cs"));
        Assert.Contains("screen.Words, colour, AfterglowLineLayer.FaceNow(), Looks.WornWordStyle.Current, Looks.WornWordStyle.Motion);", steam);
        var line = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "Services", "Compositor", "AfterglowLineLayer.cs"));
        Assert.Contains("fromX, fromY, style, Looks.WornWordStyle.Motion);", line);
        // Both draws keep the classic path for Classic.
        var ag = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "Services", "Super", "Draw", "AfterglowLineDraw.cs"));
        Assert.Contains("if (style != WordStyle.Classic) { DrawStyled(", ag);
        var sw = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "Services", "Super", "Draw", "SteamWordsDraw.cs"));
        Assert.Contains("if (style == WordStyle.Classic) Lay(", sw);
        Assert.DoesNotContain("SaveLayer", sw);
    }

    // ---- "Also show the normal subliminals" ----

    [Fact]
    public void The_normal_cards_rest_only_while_afterglow_runs_alone()
    {
        Assert.True(AfterglowBase.CardRests(afterglowRuns: true, trying: false, alsoBase: false));
        Assert.False(AfterglowBase.CardRests(true, false, alsoBase: true));    // the player asked for both
        Assert.False(AfterglowBase.CardRests(afterglowRuns: false, false, false));  // V2 off: nothing changes
        Assert.False(AfterglowBase.CardRests(true, trying: true, false));      // a weekly try is a taste
        var s = new AppSettings();
        Assert.False(s.AfterglowAlsoBase);
        s.AfterglowAlsoBase = true;
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.True(back.AfterglowAlsoBase);
    }

    [Fact]
    public void Only_the_ambient_tick_rests_the_card_and_the_word_still_leaves_its_ghost()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs"));
        var tick = Between(src, "private void Timer_Tick", "public void FlashSubliminal(");
        Assert.Contains("FlashSubliminal(restCard: AfterglowRestsCard());", tick);
        Assert.Contains("App.CompositorEnabled", tick);   // no compositor, no ghost: the card stays
        Assert.Contains("AfterglowAlsoBase", tick);
        var flash = Between(src, "public void FlashSubliminal(", "public void FlashSubliminalCustom(");
        Assert.True(flash.IndexOf("AfterglowLineLayer.WordShown(text)", StringComparison.Ordinal) < flash.IndexOf("restCard", flash.IndexOf("WordShown", StringComparison.Ordinal), StringComparison.Ordinal));
        var custom = Between(src, "public void FlashSubliminalCustom(", "private int _deferredResetPending");
        Assert.DoesNotContain("restCard", custom);
        var show = Between(src, "private void ShowSubliminalVisuals(", "private bool ShowCompositorSubliminal(");
        int counted = show.IndexOf("SubliminalDisplayed?.Invoke", StringComparison.Ordinal);
        int rests = show.IndexOf("if (restCard) return;", StringComparison.Ordinal);
        Assert.True(counted > 0 && rests > counted);
        Assert.True(rests < show.IndexOf("ShowCompositorSubliminal(screens", StringComparison.Ordinal));
    }

    [Fact]
    public void The_switch_sits_first_in_the_gold_box_rows()
    {
        var dir = Path.Combine(Root(), "ConditioningControlPanel", "Features");
        var xaml = File.ReadAllText(Path.Combine(dir, "SubliminalFeatureControl.xaml"));
        int rows = xaml.IndexOf("x:Name=\"AfterglowRows\"", StringComparison.Ordinal);
        int sw = xaml.IndexOf("x:Name=\"ChkAfterglowAlsoBase\"", StringComparison.Ordinal);
        int words = xaml.IndexOf("x:Name=\"SliderAfterglowWords\"", StringComparison.Ordinal);
        Assert.True(rows > 0 && sw > rows && sw < words);
        Assert.Contains("{loc:Str super_afterglow_also_base}", xaml);
        var code = File.ReadAllText(Path.Combine(dir, "SubliminalFeatureControl.Afterglow.cs"));
        Assert.Contains("ChkAfterglowAlsoBase.IsChecked = s.AfterglowAlsoBase;", code);
        Assert.Contains("s.AfterglowAlsoBase = ChkAfterglowAlsoBase.IsChecked == true;", code);
        foreach (var f in Directory.GetFiles(Path.Combine(Root(), "ConditioningControlPanel", "Localization", "Languages"), "*.json"))
        {
            var t = File.ReadAllText(f);
            Assert.Contains("\"super_afterglow_also_base\":", t);
            Assert.Contains("\"tooltip_super_afterglow_also_base\":", t);
        }
    }

    // ---- stills ----

    /// <summary>
    /// Proof in pictures: with CCP_WORDS_DUMP=dir, one sheet per style for Afterglow (first ghost
    /// typing in as it rises, the same a beat later, a line building, the finale, and the rising
    /// ghost under Gentle visuals) and one for the fog words (five moments of a word's life, the
    /// last under Gentle visuals). Without the variable it checks every style draws something.
    /// </summary>
    [Fact]
    public void Dump_the_v2_word_styles()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_WORDS_DUMP");
        bool dump = !string.IsNullOrEmpty(dir);
        if (dump) Directory.CreateDirectory(dir!);
        foreach (var style in Styles)
        {
            var stages = new[] { AfterglowStage.Rising, AfterglowStage.Later, AfterglowStage.Building, AfterglowStage.Finale, AfterglowStage.Rising };
            using (var sheet = Sheet(stages.Length, (i, c) =>
                   {
                       using var one = DrawAfterglow(style, i == stages.Length - 1 ? WordMotion.Still : WordMotion.Full, stages[i]);
                       c.DrawBitmap(one, 0, 0);
                       return $"Afterglow {style} {stages[i]}{(i == stages.Length - 1 ? " Gentle" : "")}";
                   }))
            {
                Assert.True(Lit(sheet) > 100, $"afterglow {style}");
                if (dump) Save(sheet, dir!, $"afterglow-{(int)style}-{style.ToString().ToLowerInvariant()}");
            }
            double[] at = { 0.2, 0.5, 1.0, 4.0, 0.2 };
            using (var sheet = Sheet(at.Length, (i, c) =>
                   {
                       using var one = DrawFog(style, i == at.Length - 1 ? WordMotion.Still : WordMotion.Full, at[i], withFog: true);
                       c.DrawBitmap(one, 0, 0);
                       return $"Fog {style} life={at[i]:0.0}s{(i == at.Length - 1 ? " Gentle" : "")}";
                   }))
            {
                Assert.True(Lit(sheet) > 100, $"fog {style}");
                if (dump) Save(sheet, dir!, $"fog-{(int)style}-{style.ToString().ToLowerInvariant()}");
            }
        }
    }

    // ---- harness ----

    internal enum AfterglowStage { Rising, Later, Building, Finale }

    private static SKBitmap DrawAfterglow(WordStyle? style, WordMotion motion, AfterglowStage stage)
    {
        const int W = 960, H = 540;
        var line = new AfterglowLine();
        string[] words = { "sink deeper", "obey", "let go", "drift" };
        switch (stage)
        {
            case AfterglowStage.Rising: line.Add(words[0]); line.Step(0.18); break;
            case AfterglowStage.Later: line.Add(words[0]); line.Step(0.45); break;
            case AfterglowStage.Building:
                for (int i = 0; i < 3; i++) { line.Add(words[i]); line.Step(1.3); }
                line.Add(words[3]); line.Step(0.3);
                break;
            case AfterglowStage.Finale:
                for (int i = 0; i < 4; i++) { line.Add(words[i]); line.Step(1.3); }
                line.Step(0.05); line.Step(0.6);
                break;
        }
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(29, 24, 38));
        using var draw = new AfterglowLineDraw();
        var bounds = new SKRect(0, 0, W, H);
        if (style == null) draw.Draw(canvas, bounds, line, Face, new SKColor(255, 105, 180), W / 2f, H * 0.46f);
        else draw.Draw(canvas, bounds, line, Face, new SKColor(255, 105, 180), W / 2f, H * 0.46f, style.Value, motion);
        canvas.Flush();
        return bmp;
    }

    private static (SteamField, SteamWords) FullFog(int w, int h)
    {
        // Wiped everywhere and fogged over again: every place wakes, the reveal mask is full.
        var field = new SteamField(w, h);
        for (int i = 0; i < field.Fog.Length; i++) { field.Fog[i] = 1; field.Mem[i] = 1; }
        var words = new SteamWords(seed: 7);
        words.SetPool(new[] { "sink", "obey", "deeper", "let go", "drift", "empty" });
        words.Line = "sink deeper  ·  let go";
        return (field, words);
    }

    private static SKBitmap DrawFog(WordStyle? style, WordMotion motion, double life, bool withFog = false)
    {
        const int W = 960, H = 540;
        var (field, words) = FullFog(W, H);
        words.Step(field, 0.0, false);
        for (double t = 0; t < life - 1e-9; t += 0.02) words.Step(field, Math.Min(0.02, life - t), false);
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(24, 28, 40));
        var bounds = new SKRect(0, 0, W, H);
        var pink = new SKColor(255, 105, 180);
        if (withFog)
        {
            using var fog = new SteamDraw();
            fog.Draw(canvas, bounds, field, pink, 0.62, Array.Empty<string>(), null, Face, 4);
        }
        using var draw = new SteamWordsDraw();
        if (style == null) draw.Draw(canvas, bounds, field, words, pink, Face);
        else draw.Draw(canvas, bounds, field, words, pink, Face, style.Value, motion);
        canvas.Flush();
        return bmp;
    }

    private static SKBitmap Sheet(int cells, Func<int, SKCanvas, string> cell)
    {
        const int W = 960, H = 540;
        int cols = 2, rows = (cells + 1) / 2;
        var sheet = new SKBitmap(new SKImageInfo(W * cols, H * rows, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(SKColors.Black);
        using var label = new SKPaint { Color = new SKColor(255, 255, 255, 150), TextSize = 16, IsAntialias = true };
        for (int i = 0; i < cells; i++)
        {
            canvas.Save();
            canvas.Translate(i % cols * W, i / cols * H);
            canvas.ClipRect(new SKRect(0, 0, W, H));
            string name = cell(i, canvas);
            canvas.DrawText(name, 8, 20, label);
            canvas.Restore();
        }
        canvas.Flush();
        return sheet;
    }

    private static int Lit(SKBitmap bmp)
    {
        int lit = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red + c.Green + c.Blue > 420) lit++;
            }
        return lit;
    }

    /// <summary>How much light a picture holds above its dark stand-in desktop (sampled), and dispose it.</summary>
    private static long InkOf(SKBitmap bmp)
    {
        using (bmp)
        {
            long ink = 0;
            for (int y = 0; y < bmp.Height; y += 2)
                for (int x = 0; x < bmp.Width; x += 2)
                {
                    var c = bmp.GetPixel(x, y);
                    ink += Math.Max(0, c.Red + c.Green + c.Blue - 92);
                }
            return ink;
        }
    }

    private static bool Same(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        var pa = a.GetPixelSpan(); var pb = b.GetPixelSpan();
        return pa.SequenceEqual(pb);
    }

    private static void Save(SKBitmap bmp, string dir, string name)
    {
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), data.ToArray());
    }

    private static string Between(string src, string from, string to)
    {
        int a = src.IndexOf(from, StringComparison.Ordinal);
        Assert.True(a >= 0, from);
        int b = src.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        Assert.True(b > a, to);
        return src.Substring(a, b - a);
    }

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "ConditioningControlPanel", "Services"))) d = d.Parent;
        return d!.FullName;
    }
}
