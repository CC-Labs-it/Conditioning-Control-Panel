using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Deck card's new rules (owner 2026-11-17): the card takes its picture's shape (DeckCardFit), it
/// turns like a real card (DeckTurn), and with words behind the card worn its back is a spiral with a
/// phrase coming at the viewer (DeckSpiralBack). Pure maths from CCP.Core.
/// </summary>
public class DeckCardRulesTests
{
    // ---- the card fits the picture ----

    [Theory]
    [InlineData(9.0 / 16)]
    [InlineData(16.0 / 9)]
    [InlineData(1.0)]
    [InlineData(4.0 / 5)]
    public void A_new_face_takes_its_own_aspect_and_keeps_the_card_s_area(double aspect)
    {
        var (w, h) = DeckCardFit.Reshape(768, 432, aspect);
        Assert.Equal(aspect, w / h, 6);
        Assert.Equal(768 * 432, w * h, 0);
    }

    [Fact]
    public void A_tall_picture_on_a_wide_card_gets_a_tall_card_with_no_side_bars()
    {
        var wide = new CardBox(400, 300, 768, 432);
        var tall = DeckCardFit.Refit(wide, 0, 9.0 / 16, new CardBox(0, 0, 1920, 1080));
        Assert.True(tall.H > tall.W);
        Assert.Equal(9.0 / 16, tall.W / tall.H, 6);
        Assert.Equal(wide.Cx, tall.Cx, 6);          // about the same centre
        Assert.Equal(wide.Cy, tall.Cy, 6);
    }

    [Fact]
    public void A_refit_card_stays_on_its_monitor_and_keeps_its_glow_inset()
    {
        var monitor = new CardBox(1920, 0, 1080, 1920);   // a portrait second screen
        var card = new CardBox(1930, 20, 600, 340);          // hard against its top-left corner
        var r = DeckCardFit.Refit(card, 8, 9.0 / 16, monitor);
        Assert.InRange(r.X, monitor.X, monitor.X + monitor.W - r.W);
        Assert.InRange(r.Y, monitor.Y, monitor.Y + monitor.H - r.H);
        Assert.Equal(9.0 / 16, (r.W - 16) / (r.H - 16), 6);   // the picture area takes the aspect, not the padding
        // A very tall strip is held inside 90% of the screen.
        var strip = DeckCardFit.Refit(new CardBox(0, 0, 800, 800), 0, 1.0 / 8, new CardBox(0, 0, 1920, 1080));
        Assert.True(strip.H <= 1080 * DeckCardFit.MaxShare + 1e-6);
        Assert.True(strip.W >= DeckCardFit.MinSide - 1e-6);
    }

    [Fact]
    public void The_shape_eases_from_old_to_new_without_a_jump()
    {
        var a = new CardBox(0, 0, 400, 225);
        var b = new CardBox(80, -60, 240, 426);
        Assert.Equal(a, DeckCardFit.Between(a, b, 0));
        Assert.Equal(b, DeckCardFit.Between(a, b, 1));
        double last = a.H;
        for (int i = 1; i <= 50; i++)
        {
            var m = DeckCardFit.Between(a, b, i / 50.0);
            Assert.True(m.H >= last - 1e-9);                 // grows the whole way, never back
            Assert.True(m.H - last < (b.H - a.H) * 0.06);    // and no step larger than a few percent
            last = m.H;
        }
        Assert.Equal(0.5, DeckCardFit.Progress(0.1, 0.2), 9);
        Assert.Equal(1, DeckCardFit.Progress(5, 0.2), 9);
    }

    // ---- the turn ----

    [Fact]
    public void The_turn_starts_at_rest_goes_edge_on_exactly_half_way_and_lands_flat()
    {
        Assert.Equal(0, DeckTurn.Angle(0), 9);
        Assert.Equal(Math.PI / 2, DeckTurn.Angle(0.5), 9);
        Assert.Equal(0, DeckTurn.Angle(1), 9);
        // Eased in: the first tenth of the closing half barely moves; the last tenth moves the most.
        double first = DeckTurn.Angle(0.05) - DeckTurn.Angle(0), last = DeckTurn.Angle(0.5) - DeckTurn.Angle(0.45);
        Assert.True(last > first * 8);
        // Eased out: the final tenth is a small settle, not a stop from full speed.
        Assert.True(Math.Abs(DeckTurn.Angle(1) - DeckTurn.Angle(0.95)) < 0.06);
    }

    [Fact]
    public void The_old_face_closes_monotonically_and_the_swap_is_edge_on()
    {
        double prev = -1;
        for (int i = 0; i <= 100; i++)
        {
            double a = DeckTurn.Angle(0.5 * i / 100.0);
            Assert.True(a >= prev - 1e-12);
            prev = a;
        }
        // Just past the middle the new face is at the other edge: width near nothing on both sides of the swap.
        Assert.True(DeckTurn.Width(DeckTurn.Angle(0.499), 0) < 0.01);
        Assert.True(DeckTurn.Width(DeckTurn.Angle(0.501), 0) < 0.02);
        Assert.True(DeckTurn.Angle(0.501) < 0);               // the new face comes from the other side
        // The card is never thin for long: under a fifth of its width for under a sixth of the flip.
        int thin = Enumerable.Range(0, 1001).Count(i => DeckTurn.Width(DeckTurn.Angle(i / 1000.0), 0) < 0.2);
        Assert.True(thin < 170, $"thin for {thin}/1000");
    }

    [Fact]
    public void Reduced_halves_the_swing_the_perspective_and_the_shade()
    {
        double full = Enumerable.Range(501, 500).Max(i => DeckTurn.Angle(i / 1000.0, 1));
        double half = Enumerable.Range(501, 500).Max(i => DeckTurn.Angle(i / 1000.0, 0.5));
        Assert.InRange(half / full, 0.4, 0.6);
        Assert.Equal(DeckTurn.PerspectiveTerm(0.7, 200, 1) / 2, DeckTurn.PerspectiveTerm(0.7, 200, 0.5), 12);
        Assert.Equal(DeckTurn.Shade(0.7, 1) / 2, DeckTurn.Shade(0.7, 0.5), 12);
        Assert.Equal(0, DeckTurn.Shade(0), 12);
        Assert.Equal(DeckTurn.ShadeMax, DeckTurn.Shade(Math.PI / 2), 12);
        Assert.True(DeckTurn.PerspectiveTerm(0.5, 100) > 0 && DeckTurn.PerspectiveTerm(-0.5, 100) < 0);
    }

    [Fact]
    public void Motion_off_is_a_cross_fade_and_gentle_only_lengthens_it()
    {
        Assert.Equal(1, DeckTurn.FadeOut(0, false), 9);
        Assert.Equal(0, DeckTurn.FadeOut(DeckTurn.FadeSec, false), 9);
        Assert.True(DeckTurn.FadeLength(true) > DeckTurn.FadeLength(false));
        Assert.True(DeckTurn.FadeLength(true) >= 0.3);
        double prev = 1;
        for (double t = 0; t < 0.6; t += 0.01) { double v = DeckTurn.FadeOut(t, true); Assert.True(v <= prev + 1e-12); prev = v; }
    }

    // ---- the spiral back and its phrase ----

    [Fact]
    public void The_phrase_starts_small_and_far_grows_toward_you_then_passes_and_fades()
    {
        var start = DeckSpiralBack.Word(0.01, 1, false, false);
        Assert.True(start.Visible);
        Assert.True(start.Scale < 0.25);
        double[] ages = { 0.1, 0.3, 0.5, 0.7, DeckSpiralBack.ZoomSec - 0.01 };
        double prev = 0;
        foreach (var t in ages) { var p = DeckSpiralBack.Word(t, 1, false, false); Assert.True(p.Scale > prev); prev = p.Scale; }
        var full = DeckSpiralBack.Word(DeckSpiralBack.ZoomSec + 0.01, 1, false, false);
        Assert.InRange(full.Scale, 0.95, 1.01);
        Assert.Equal(1, full.Alpha, 6);
        var passing = DeckSpiralBack.Word(DeckSpiralBack.ZoomSec + DeckSpiralBack.HoldSec + DeckSpiralBack.OutSec * 0.7, 1, false, false);
        Assert.True(passing.Scale > 1.05);
        Assert.True(passing.Alpha < 0.5);
        Assert.True(passing.Split > 0 && passing.GhostAlpha > 0);
        Assert.False(DeckSpiralBack.Word(DeckSpiralBack.Life(false, false), 1, false, false).Visible);
        // The slot's wobble on the way in: tilted one way, then the other, level at the end of the zoom.
        Assert.True(DeckSpiralBack.Word(0.4 * DeckSpiralBack.ZoomSec, 1, false, false).RotRad < 0);
        Assert.True(DeckSpiralBack.Word(0.72 * DeckSpiralBack.ZoomSec, 1, false, false).RotRad > 0);
        Assert.Equal(0, DeckSpiralBack.Word(DeckSpiralBack.ZoomSec - 0.001, 1, false, false).RotRad, 3);
    }

    [Fact]
    public void Reduced_comes_from_half_as_far_off_fades_in_place_and_gentle_rises_slowly_with_no_ghosts()
    {
        var full = DeckSpiralBack.Word(0.01, 1, false, false);
        var half = DeckSpiralBack.Word(0.01, 0.5, false, false);
        Assert.InRange(1 - half.Scale, (1 - full.Scale) * 0.45, (1 - full.Scale) * 0.55);
        for (double t = 0; t < DeckSpiralBack.Life(true, false); t += 0.05)
        {
            var s = DeckSpiralBack.Word(t, 1, true, false);
            Assert.Equal(1, s.Scale);
            Assert.Equal(0, s.RotRad);
            Assert.Equal(0, s.GhostAlpha);
        }
        Assert.True(DeckSpiralBack.Word(0.29, 1, false, true).Alpha < 1);   // the 300 ms rise floor
        Assert.True(DeckSpiralBack.Word(0.29, 1, true, false).Alpha < 1);
        for (double t = 0; t < DeckSpiralBack.Life(false, true); t += 0.05)
            Assert.Equal(0, DeckSpiralBack.Word(t, 1, false, true).GhostAlpha);
        Assert.Equal(0, DeckSpiralBack.TurnRate(true, false, 1));
        Assert.True(DeckSpiralBack.TurnRate(false, true, 1) < DeckSpiralBack.TurnRate(false, false, 1));
    }

    [Theory]
    [InlineData("sink", 1)]
    [InlineData("good girls obey", 2)]
    [InlineData("you love to sink deeper for me", 3)]
    [InlineData("supercalifragilistic", 1)]
    public void A_long_phrase_wraps_onto_up_to_three_even_lines(string text, int lines)
    {
        var l = DeckSpiralBack.Lines(text);
        Assert.Equal(lines, l.Length);
        Assert.Equal(text, string.Join(' ', l));
        if (lines > 1) Assert.True(l.Max(x => x.Length) - l.Min(x => x.Length) <= 10);
    }

    [Fact]
    public void The_phrase_fits_any_card_shape_even_as_it_passes()
    {
        foreach (var (w, h) in new[] { (243.0, 432.0), (768.0, 432.0), (120.0, 600.0), (900.0, 120.0) })
        {
            double widest = 7.2;   // a long line, 7.2 font sizes wide
            double font = DeckSpiralBack.FontPx(widest, 2, w, h);
            Assert.True(font * widest * DeckSpiralBack.PassScale <= w * DeckSpiralBack.FitW + 1e-6 || font <= 6);
            Assert.True(font * 2 * 1.08 * DeckSpiralBack.PassScale <= h * DeckSpiralBack.FitH + 1e-6 || font <= 6);
        }
    }

    [Fact]
    public void Every_flip_to_the_back_rolls_a_new_spiral_in_the_mod_s_colours()
    {
        var rng = new Random(11);
        uint accent = 0x33CCFF;
        LoomSpec? last = null;
        for (int i = 0; i < 40; i++)
        {
            var s = DeckSpiralBack.Roll(rng, accent, last);
            Assert.Equal(accent, s.Layer.Colors[0]);                       // the accent leads
            var pal = DeckSpiralBack.Palette(accent);
            Assert.All(s.Layer.Colors, c => Assert.Contains(c, pal));
            Assert.Equal(0, s.PulseAmp);
            Assert.Equal(0, s.WobAmp);
            if (last != null) Assert.False(DeckSpiralBack.SameShape(s, last), $"flip {i} repeated its spiral");
            last = s;
        }
        Assert.Equal(0xFF000000u, DeckSpiralBack.Ground(accent) & 0xFF000000u);
    }

    [Fact]
    public void A_back_spiral_fills_its_bitmap_with_no_see_through_pixels()
    {
        var loom = new LoomSpiral(64);
        loom.Add(DeckSpiralBack.Roll(new Random(2), 0xFF69B4), 0, 0, 1);
        var px = new byte[64 * 64 * 4];
        loom.CopyToBgraOver(px, 0x201020);
        for (int i = 3; i < px.Length; i += 4) Assert.Equal(255, px[i]);
        Assert.Contains(px.Where((_, i) => i % 4 == 2), b => b > 100);   // some spiral red shows (BGRA: index 2 is red)
    }
}
