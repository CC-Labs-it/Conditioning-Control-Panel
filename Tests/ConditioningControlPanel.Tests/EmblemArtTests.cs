using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Emblems;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows emblems (owner 2026-11-24): the art ported from the mockup's E table, its colour
/// maths and the leading-emoji strip. Pure CCP.Core. With env CCP_EMBLEM_DUMP=&lt;dir&gt; the contact
/// sheet test writes emblems.svg there for review.
/// </summary>
public class EmblemArtTests
{
    public static IEnumerable<object[]> KindsAndSubjects() =>
        from k in Enum.GetValues<EmblemKind>()
        from s in Enum.GetValues<EmblemSubject>()
        select new object[] { k, s };

    [Theory]
    [MemberData(nameof(KindsAndSubjects))]
    public void Every_kind_and_subject_draws_layers_that_parse_inside_the_grid(EmblemKind kind, EmblemSubject subject)
    {
        foreach (double value in new[] { 0, 0.5, 1, 3, 8 })
        {
            var layers = EmblemArt.Layers(kind, subject, value);
            Assert.NotEmpty(layers);
            foreach (var layer in layers)
            {
                Assert.False(string.IsNullOrWhiteSpace(layer.PathData));
                Assert.Matches("^[MLCQAZ0-9., -]+$", layer.PathData); // absolute commands only, invariant numbers
                var pts = EmblemPath.Points(layer.PathData);
                Assert.NotEmpty(pts);
                foreach (var (x, y) in pts)
                {
                    Assert.InRange(x, -2, 34);
                    Assert.InRange(y, -2, 34);
                }
                Assert.True(layer.StrokeWidth >= 0);
                Assert.Equal(layer.Ink == EmblemInk.Fixed, layer.FixedArgb.HasValue);
            }
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    [InlineData(6, 4)]
    [InlineData(15, 4)]
    [InlineData(0, 3)] // unset: the mockup's default
    public void Many_counts_items_like_the_mockup(double value, int items)
    {
        Assert.Equal(items, EmblemArt.ManyCount(value));
        // Each image item is two layers in front (paper + picture) and one at the back (soft).
        var layers = EmblemArt.Layers(EmblemKind.Many, EmblemSubject.Image, value);
        Assert.Equal((items - 1) + 2, layers.Count);
        Assert.Equal(items - 1, layers.Count(l => l.Ink == EmblemInk.Soft));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(20, 8)]
    [InlineData(0, 8)]
    public void Most_draws_min_value_8_small_rects_on_the_monitor(double value, int rects)
    {
        Assert.Equal(rects, EmblemArt.MostCount(value));
        var layers = EmblemArt.Layers(EmblemKind.Most, EmblemSubject.Image, value);
        Assert.Equal(4 + rects, layers.Count); // monitor = stand, foot, frame, screen
    }

    [Fact]
    public void Often_turns_its_hand_with_the_fraction_and_a_full_turn_comes_back()
    {
        string Hand(double v) => EmblemArt.Layers(EmblemKind.Often, EmblemSubject.Image, v).Single(l => l.IsStroke).PathData;
        Assert.Equal("M16,18 L16,12.4", Hand(0));
        Assert.Equal(Hand(0), Hand(1));
        Assert.Equal("M16,18 L21.6,18", Hand(0.25)); // a quarter turn points right (clockwise on screen)
    }

    [Fact]
    public void Subject_changes_only_the_stacks()
    {
        var many = Enum.GetValues<EmblemSubject>().Select(s => string.Join("|", EmblemArt.Layers(EmblemKind.Many, s, 3).Select(l => l.PathData))).ToList();
        Assert.Equal(4, many.Distinct().Count());
        var eye = Enum.GetValues<EmblemSubject>().Select(s => string.Join("|", EmblemArt.Layers(EmblemKind.Eye, s, 3).Select(l => l.PathData))).ToList();
        Assert.Single(eye.Distinct());
        Assert.Equal(EmblemArt.ShapeKey(EmblemKind.Eye, EmblemSubject.Video, 3), EmblemArt.ShapeKey(EmblemKind.Eye, EmblemSubject.Bubble, 9));
        Assert.Equal(EmblemArt.ShapeKey(EmblemKind.Many, EmblemSubject.Video, 3), EmblemArt.ShapeKey(EmblemKind.Many, EmblemSubject.Video, 5));
        Assert.NotEqual(EmblemArt.ShapeKey(EmblemKind.Many, EmblemSubject.Video, 3), EmblemArt.ShapeKey(EmblemKind.Many, EmblemSubject.Image, 3));
    }

    [Fact]
    public void Path_normaliser_bakes_rects_circles_and_transforms()
    {
        Assert.Equal("M0,0 L2,0 L2,1 L0,1 Z", EmblemPath.Rect(0, 0, 2, 1));
        // translate(10 0) rotate(90) on (1, 0) -> (10, 1)
        var m = Affine.Group(10, 0, 90, 1);
        Assert.Equal("M10,1 L10,2", EmblemPath.Transform("M1 0h1", m));
        // relative, implicit repeats, shorthand S and arcs survive the round trip
        var pts = EmblemPath.Points(EmblemPath.Transform("M2 2c1 0 2 1 2 2s1 2 2 2a1 1 0 0 1 2 0z", Affine.Identity));
        Assert.Equal((8.0, 6.0), pts.Last());
        Assert.Throws<FormatException>(() => EmblemPath.Points("M1 2 L"));
    }

    // ---------- palette ----------

    [Fact]
    public void Mix_ends_are_exact_and_the_middle_is_between()
    {
        uint a = 0xFFFF69B4, b = 0xFFFFFFFF;
        Assert.Equal(a, EmblemPalette.Mix(a, b, 0));
        Assert.Equal(b, EmblemPalette.Mix(a, b, 1));
        Assert.Equal(a, EmblemPalette.Mix(a, a, 0.37));
        uint mid = EmblemPalette.Mix(0xFF000000, 0xFFFFFFFF, 0.5);
        // OKLab mid grey is lighter than the sRGB average would be: about #636363
        Assert.Equal(EmblemPalette.R(mid), EmblemPalette.G(mid));
        Assert.InRange((int)EmblemPalette.R(mid), 0x5A, 0x6C);
    }

    [Fact]
    public void Sticker_inks_follow_the_hue()
    {
        uint pink = EmblemPalette.Pink;
        Assert.Equal(pink, EmblemPalette.Ink(EmblemInk.Main, pink));
        Assert.Equal(pink, EmblemPalette.Ink(EmblemInk.Pic, pink));
        Assert.Equal(0xFFFFFFFFu, EmblemPalette.Ink(EmblemInk.Cut, pink));
        Assert.Equal(0xFFFFFFFFu, EmblemPalette.Ink(EmblemInk.Paper, pink));
        Assert.Equal(0xFFFFD35Cu, EmblemPalette.Ink(EmblemInk.Tape, pink));
        Assert.Equal(0xEB5CE0A5u, EmblemPalette.Ink(EmblemInk.Fixed, pink, 0xEB5CE0A5));
        uint soft = EmblemPalette.Ink(EmblemInk.Soft, pink);
        Assert.Equal(EmblemPalette.Mix(pink, 0xFFFFFFFF, 0.58), soft);
        Assert.True(EmblemPalette.G(soft) > EmblemPalette.G(pink)); // lighter
    }

    [Fact]
    public void Label_and_value_tints_are_the_owner_mixes()
    {
        uint pink = EmblemPalette.Pink, light = EmblemPalette.TextLight;
        Assert.Equal(EmblemPalette.Mix(light, pink, 0.30), EmblemPalette.LabelTint(light, pink));
        Assert.Equal(EmblemPalette.Mix(pink, 0xFFFFFFFF, 0.18), EmblemPalette.ValueTint(pink));
        uint label = EmblemPalette.LabelTint(light, pink);
        // Still near white: green channel well above the pink's 0x69.
        Assert.InRange((int)EmblemPalette.G(label), 0xB0, 0xEE);
        uint value = EmblemPalette.ValueTint(pink);
        Assert.InRange((int)EmblemPalette.G(value), 0x69 + 1, 0xC0);
    }

    [Fact]
    public void Off_ink_is_grey_and_keeps_alpha()
    {
        uint g = EmblemPalette.OffInk(EmblemInk.Fixed, EmblemPalette.Pink, 0xEB5EA8FF);
        Assert.Equal(EmblemPalette.R(g), EmblemPalette.G(g));
        Assert.Equal(EmblemPalette.G(g), EmblemPalette.B(g));
        Assert.Equal(0xEB, EmblemPalette.A(g));
        Assert.Equal(0xFFFFFFFFu, EmblemPalette.Grey(0xFFFFFFFF));
        Assert.Equal(0.45, EmblemPalette.OffEmblemOpacity);
        Assert.Equal(0.6, EmblemPalette.OffLabelOpacity);
    }

    // ---------- emoji strip ----------

    [Theory]
    [InlineData("\U0001F419Hydra", "Hydra")]
    [InlineData("\U0001F517 Linked", "Linked")]
    [InlineData("⚡ Flash Images", "Flash Images")]
    [InlineData("❤️ Love", "Love")]
    [InlineData("\U0001F44D\U0001F3FD Ok", "Ok")]
    [InlineData("\U0001F468‍\U0001F469‍\U0001F467 Family", "Family")]
    [InlineData("\U0001F1EE\U0001F1F9 Italiano", "Italiano")]
    [InlineData("3️⃣ Tre", "Tre")]
    [InlineData("  \U0001F419  Idra", "Idra")]
    [InlineData("\U0001F419 Idra (collegate)", "Idra (collegate)")]       // it
    [InlineData("\U0001F419 Hydre", "Hydre")]                               // fr
    [InlineData("\U0001F419 Hydra-Modus", "Hydra-Modus")]                   // de
    [InlineData("\U0001F517 Vinculadas", "Vinculadas")]                     // es
    [InlineData("\U0001F517 Ligadas à próxima", "Ligadas à próxima")]       // pt
    [InlineData("\U0001F419 Гидра", "Гидра")]                               // ru
    [InlineData("\U0001F419 ヒドラ", "ヒドラ")]                              // ja
    [InlineData("\U0001F419 히드라", "히드라")]                              // ko
    [InlineData("\U0001F419 九头蛇", "九头蛇")]                              // zh
    public void Strip_drops_one_leading_emoji_and_its_space(string input, string expected) =>
        Assert.Equal(expected, LeadingEmoji.Strip(input));

    [Theory]
    [InlineData("Hydra")]
    [InlineData("画像の数")]
    [InlineData("이미지 수")]
    [InlineData("每小时")]
    [InlineData("Изображения")]
    [InlineData("Ñandú")]
    [InlineData("3 images")]
    [InlineData("#tag")]
    [InlineData("Hydra \U0001F419")]
    [InlineData("\U0001F419")] // emoji-only keeps its emoji
    public void Strip_leaves_other_labels_alone(string input) =>
        Assert.Equal(input, LeadingEmoji.Strip(input));

    [Fact]
    public void Strip_of_nothing_is_empty()
    {
        Assert.Equal("", LeadingEmoji.Strip(null));
        Assert.Equal("", LeadingEmoji.Strip(""));
    }

    // ---------- contact sheet ----------

    [Fact]
    public void Contact_sheet_dump_when_asked()
    {
        var cells = new List<(string Name, IReadOnlyList<EmblemLayer> Layers)>();
        foreach (var k in Enum.GetValues<EmblemKind>())
        {
            double v = k switch { EmblemKind.Often => 0.3, EmblemKind.Many => 3, EmblemKind.Most => 8, _ => 0 };
            cells.Add((k.ToString(), EmblemArt.Layers(k, EmblemSubject.Image, v)));
        }
        foreach (var s in Enum.GetValues<EmblemSubject>())
            foreach (int n in new[] { 1, 2, 4, 8 })
                cells.Add(($"Many {s} {n}", EmblemArt.Layers(EmblemKind.Many, s, n)));
        foreach (var s in new[] { EmblemSubject.Video, EmblemSubject.Bubble, EmblemSubject.Word })
            cells.Add(($"Splits {s}", EmblemArt.Layers(EmblemKind.Splits, s, 0)));

        string svg = ContactSheet(cells, EmblemPalette.Pink, 0xFF1F1D3A);
        Assert.Contains("<svg", svg);

        string? dir = Environment.GetEnvironmentVariable("CCP_EMBLEM_DUMP");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "emblems.svg");
        File.WriteAllText(path, svg, new UTF8Encoding(false));
        Assert.True(File.Exists(path));
    }

    private static string ContactSheet(List<(string Name, IReadOnlyList<EmblemLayer> Layers)> cells, uint hue, uint bg)
    {
        const int cols = 8, cw = 120, ch = 112;
        const double scale = 2.0; // 64 px emblems
        int rows = (cells.Count + cols - 1) / cols;
        var ci = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.Append(ci, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{cols * cw}\" height=\"{rows * ch}\" viewBox=\"0 0 {cols * cw} {rows * ch}\">\n");
        b.Append($"<rect width=\"100%\" height=\"100%\" fill=\"{Css(bg)}\"/>\n");
        double edge = EmblemPalette.StickerEdgeDip * 32 / 34; // the mockup's 1.1 px at a 34 px emblem, in grid units
        for (int i = 0; i < cells.Count; i++)
        {
            var (name, layers) = cells[i];
            double cx = (i % cols) * cw + cw / 2.0, cy = (i / cols) * ch + 44;
            string place = string.Format(ci, "translate({0} {1}) rotate({2}) scale({3}) translate(-16 -16)", cx, cy, EmblemPalette.StickerTiltDegrees, scale);
            b.Append(ci, $"<g transform=\"{place}\">\n");
            // pass 1: shadow (the edged silhouette, offset down), pass 2: white edge, pass 3: inks
            b.Append(ci, $"<g opacity=\"{EmblemPalette.StickerShadowOpacity}\" transform=\"translate(0 {EmblemPalette.StickerShadowDyDip * 32 / 34})\">");
            foreach (var l in layers) b.Append(Silhouette(l, edge, "#000"));
            b.Append("</g>\n<g>");
            foreach (var l in layers) b.Append(Silhouette(l, edge, "#fff"));
            b.Append("</g>\n");
            foreach (var l in layers)
            {
                string col = Css(EmblemPalette.Ink(l.Ink, hue, l.FixedArgb));
                double op = EmblemPalette.A(EmblemPalette.Ink(l.Ink, hue, l.FixedArgb)) / 255.0;
                b.Append(l.IsStroke
                    ? string.Format(ci, "<path d=\"{0}\" fill=\"none\" stroke=\"{1}\" stroke-width=\"{2}\" stroke-linejoin=\"round\" stroke-linecap=\"{3}\" opacity=\"{4:0.##}\"/>", l.PathData, col, l.StrokeWidth, l.RoundCaps ? "round" : "butt", op)
                    : string.Format(ci, "<path d=\"{0}\" fill=\"{1}\" opacity=\"{2:0.##}\"/>", l.PathData, col, op));
            }
            b.Append("</g>\n");
            b.Append(ci, $"<text x=\"{cx}\" y=\"{(i / cols) * ch + 100}\" fill=\"#f0f0f5\" font-family=\"Nunito, Segoe UI, sans-serif\" font-weight=\"800\" font-size=\"13\" text-anchor=\"middle\">{name}</text>\n");
        }
        b.Append("</svg>\n");
        return b.ToString();
    }

    private static string Silhouette(EmblemLayer l, double edge, string col)
    {
        var ci = CultureInfo.InvariantCulture;
        return l.IsStroke
            ? string.Format(ci, "<path d=\"{0}\" fill=\"none\" stroke=\"{1}\" stroke-width=\"{2}\" stroke-linejoin=\"round\" stroke-linecap=\"{3}\"/>", l.PathData, col, l.StrokeWidth + 2 * edge, l.RoundCaps ? "round" : "butt")
            : string.Format(ci, "<path d=\"{0}\" fill=\"{1}\" stroke=\"{1}\" stroke-width=\"{2}\" stroke-linejoin=\"round\"/>", l.PathData, col, 2 * edge);
    }

    private static string Css(uint argb) => $"#{EmblemPalette.R(argb):x2}{EmblemPalette.G(argb):x2}{EmblemPalette.B(argb):x2}";
}
