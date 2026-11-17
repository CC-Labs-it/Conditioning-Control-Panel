using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;
using static ConditioningControlPanel.Tests.DeckLooksTests;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Shop looks on the Deck (lane J1): the whisper behind the card (slot <c>behind</c>). The default row
/// shows none. Set <c>CCP_DECK_DUMP</c> to a folder to write a flip's frames.
/// </summary>
public class DeckWhisperTests
{
    private static readonly (double, double) Right = DeckWhisper.Direction(0);

    [Fact]
    public void Only_the_whisper_row_puts_a_word_behind_the_card()
    {
        Assert.False(DeckWhisper.On(WornLooks.DefaultOf(WornLooks.Behind)));
        Assert.False(DeckWhisper.On(null));
        Assert.False(DeckWhisper.On("fx.card.ink"));
        Assert.True(DeckWhisper.On("fx.behind.whisper"));
    }

    [Fact]
    public void Behind_as_the_card_turns_past_it_after_and_gone_in_about_a_second()
    {
        var turning = DeckWhisper.Sample(FlickerDeck.FlipSec / 2, 1, false, false, Right);
        Assert.True(turning.Visible);
        Assert.False(turning.Front);
        Assert.True(turning.Scale < 1);
        var past = DeckWhisper.Sample(0.5, 1, false, false, Right);
        Assert.True(past.Front);
        Assert.True(past.Scale > turning.Scale);
        Assert.Equal(FlickerDeck.FlipSec, DeckWhisper.FrontAtSec);
        Assert.False(DeckWhisper.Sample(DeckWhisper.LifeSec, 1, false, false, Right).Visible);
        Assert.True(DeckWhisper.LifeSec <= 1.25);
        // Fading out with its ghosts before it goes, never brighter than a whisper.
        var late = DeckWhisper.Sample(1.1, 1, false, false, Right);
        Assert.True(late.Split > 0 && late.Alpha < past.Alpha);
        for (double t = 0; t < 1.3; t += 0.02)
            Assert.InRange(DeckWhisper.Sample(t, 1, false, false, Right).Alpha, 0, DeckWhisper.Strength);
    }

    [Fact]
    public void Never_over_the_picture_s_centre_for_long()
    {
        // In front of the card, the word is already on its way out; soon after, it is off the middle.
        for (double t = DeckWhisper.FrontAtSec + 0.15; t < DeckWhisper.LifeSec; t += 0.05)
        {
            var p = DeckWhisper.Sample(t, 0.5, false, false, Right);
            Assert.True(Math.Abs(p.Dx) >= 0.25, $"t={t}: dx {p.Dx}");
            Assert.True(p.Dy < -0.2, $"t={t}: dy {p.Dy}");
        }
        Assert.True(DeckWhisper.Direction(1).X < 0 && DeckWhisper.Direction(2).X > 0);   // flips take turns
    }

    [Fact]
    public void Motion_off_shows_none_reduced_halves_the_growth_and_gentle_only_fades()
    {
        for (double t = 0; t < 2; t += 0.05) Assert.False(DeckWhisper.Sample(t, 1, true, false, Right).Visible);
        var full = DeckWhisper.Sample(1.0, 1, false, false, Right);
        var half = DeckWhisper.Sample(1.0, 0.5, false, false, Right);
        Assert.Equal((full.Scale - DeckWhisper.ScaleFrom) / 2, half.Scale - DeckWhisper.ScaleFrom, 6);
        Assert.Equal(full.Split / 2, half.Split, 6);

        WhisperPose? first = null;
        for (double t = 0; t < DeckWhisper.GentleLifeSec; t += 0.05)
        {
            var g = DeckWhisper.Sample(t, 0.5, false, true, Right);
            Assert.True(g.Visible && g.Front);
            Assert.Equal(1, g.Scale);
            Assert.Equal(0, g.Split);
            first ??= g;
            Assert.Equal(first.Value.Dx, g.Dx);
            Assert.Equal(first.Value.Dy, g.Dy);
        }
        // No rise faster than the Gentle floor (300 ms to full).
        Assert.True(DeckWhisper.Sample(0.29, 1, false, true, Right).Alpha < DeckWhisper.Strength);
        Assert.False(DeckWhisper.Sample(DeckWhisper.GentleLifeSec, 1, false, true, Right).Visible);
    }

    [Fact]
    public void The_word_comes_from_the_player_s_own_ticked_words()
    {
        var rng = new Random(3);
        Assert.Null(DeckWhisper.Pick(null, null, rng));
        Assert.Null(DeckWhisper.Pick(new Dictionary<string, bool> { ["off"] = false, [" "] = true }, null, rng));
        var pool = new Dictionary<string, bool>
        {
            ["Sink"] = true, ["deeper"] = true, ["skipped"] = false, ["a word far too long to whisper"] = true,
        };
        for (int i = 0; i < 40; i++)
        {
            var w = DeckWhisper.Pick(pool, "sink", rng);
            Assert.Equal("deeper", w);   // never the word this card just showed while there is another
        }
        Assert.Equal("sink", DeckWhisper.Pick(new Dictionary<string, bool> { ["Sink"] = true }, "sink", rng));
    }

    [Fact]
    public void Stills_of_a_whisper_across_a_flip()
    {
        using var draw = new DeckLookDraw();
        double[] ts = { 0.08, FlickerDeck.FlipSec / 2, 0.36, 0.7, 1.05 };
        using var plain = Strip(draw, ts, DeckBack.Plain, gentle: false);
        using var ink = Strip(draw, ts, DeckBack.Ink, gentle: false);
        using var gentle = Strip(draw, new[] { 0.1, 0.3, 0.6, 1.0, 1.3 }, DeckBack.Plain, gentle: true);
        var dir = Environment.GetEnvironmentVariable("CCP_DECK_DUMP");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            Save(plain, Path.Combine(dir, "whisper-plain.png"));
            Save(ink, Path.Combine(dir, "whisper-ink.png"));
            Save(gentle, Path.Combine(dir, "whisper-gentle.png"));
        }
        // Frame 4: the word is in front, out past the card's upper right edge.
        var r = Box(3);
        var p = DeckWhisper.Sample(ts[3], 1, false, false, Right);
        var at = Pixel(plain, (int)(r.MidX + p.Dx * CardW), (int)(r.MidY + p.Dy * CardH));
        Assert.NotEqual(Bg, at);
    }

    /// <summary>The flash layer's order: the word behind, the card turning, the word in front.</summary>
    private static SKSurface Strip(DeckLookDraw draw, double[] ts, DeckBack back, bool gentle)
    {
        var s = Surface(Cell * ts.Length, out var c);
        for (int i = 0; i < ts.Length; i++)
        {
            var r = Box(i);
            var p = DeckWhisper.Sample(ts[i], 1, false, gentle, Right);
            if (!p.Front) draw.DrawWhisper(c, r.MidX, r.MidY, CardW, CardH, "deeper", p, 255);
            double flipT = ts[i] <= FlickerDeck.FlipSec ? ts[i] : -1;
            FlipFrame(c, draw, r, flipT, back, ts[i] < FlickerDeck.FlipSec / 2 ? 320 : 190);
            if (p.Front) draw.DrawWhisper(c, r.MidX, r.MidY, CardW, CardH, "deeper", p, 255);
        }
        return s;
    }
}
