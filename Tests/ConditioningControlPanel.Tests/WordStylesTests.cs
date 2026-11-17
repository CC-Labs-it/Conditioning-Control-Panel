using System;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Services.Looks;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Word styles (shop slot <c>word</c>): the one rule in CCP.Core/Looks/WordStyles and the
/// subliminal card that draws it. Classic is today's card; a style never changes the life.
/// </summary>
public class WordStylesTests
{
    private static readonly WordStyle[] Styled = { WordStyle.Type, WordStyle.Chalk, WordStyle.Drift, WordStyle.Echo, WordStyle.Marquee };

    [Theory]
    [InlineData(null, WordStyle.Classic)]
    [InlineData("fx.word.classic", WordStyle.Classic)]
    [InlineData("fx.word.type", WordStyle.Type)]
    [InlineData("fx.word.chalk", WordStyle.Chalk)]
    [InlineData("fx.word.drift", WordStyle.Drift)]
    [InlineData("fx.word.echo", WordStyle.Echo)]
    [InlineData("fx.word.marquee", WordStyle.Marquee)]
    [InlineData("fx.word.nope", WordStyle.Classic)]
    public void A_worn_row_names_its_style(string? row, WordStyle style) => Assert.Equal(style, WordStyles.Of(row));

    [Fact]
    public void The_free_row_is_classic() => Assert.Equal(WordStyle.Classic, WordStyles.Of(WornLooks.DefaultOf(WornLooks.Word)));

    [Fact]
    public void Classic_keeps_the_surface_face_and_colour_and_fills_a_plain_word()
    {
        var look = WordStyles.FaceOf(WordStyle.Classic);
        Assert.Null(look.Families); Assert.Null(look.Colour); Assert.Null(look.Border);
        Assert.Equal(1.0, look.Size); Assert.Equal(0, look.Tilt);
        var f = new WordFrame();
        for (double t = 0; t <= 4; t += 0.25)
        {
            WordStyles.Fill(WordStyle.Classic, 5, t, 4, WordMotion.Full, f);
            for (int i = 0; i < 5; i++) { Assert.Equal(1, f.Letters[i].Alpha); Assert.Equal(0, f.Letters[i].Dy); Assert.Equal(1, f.Letters[i].Reveal); }
            Assert.Equal(0, f.GhostCount); Assert.Equal(0, f.CursorAlpha);
        }
    }

    [Theory]
    [InlineData(0.2)]    // the default subliminal: 100 ms hold and two 50 ms fades
    [InlineData(4.0)]
    [InlineData(8.3)]    // a fog word
    public void Every_style_is_fully_in_by_half_its_life_and_starts_hidden_where_it_writes_on(double life)
    {
        var f = new WordFrame();
        foreach (var s in Styled)
        {
            WordStyles.Fill(s, 12, life * 0.5, life, WordMotion.Full, f);
            for (int i = 0; i < 12; i++)
            {
                Assert.True(f.Letters[i].Alpha >= 0.44, $"{s} letter {i} at half life: {f.Letters[i].Alpha}");
                Assert.Equal(1, f.Letters[i].Reveal, 3);
            }
            WordStyles.Fill(s, 12, 0, life, WordMotion.Full, f);
            if (s == WordStyle.Type || s == WordStyle.Drift) Assert.Equal(0, f.Letters[11].Alpha, 3);
            if (s == WordStyle.Chalk) Assert.Equal(0, f.Letters[0].Reveal, 3);
            if (s == WordStyle.Marquee) { Assert.Equal(0, f.Letters[11].Lit, 3); Assert.Equal(1, f.Letters[11].Alpha, 3); }
        }
    }

    [Fact]
    public void Still_motion_is_the_settled_word_with_no_caret_and_no_blink()
    {
        var f = new WordFrame();
        foreach (var s in Styled)
            for (double t = 0; t <= 4; t += 0.01)
            {
                WordStyles.Fill(s, 6, t, 4, WordMotion.Still, f);
                for (int i = 0; i < 6; i++)
                {
                    Assert.Equal(1, f.Letters[i].Alpha); Assert.Equal(0, f.Letters[i].Dy);
                    Assert.Equal(1, f.Letters[i].Reveal); Assert.Equal(1, f.Letters[i].Lit);
                }
                Assert.Equal(0, f.CursorAlpha);
            }
    }

    [Fact]
    public void Only_full_motion_blinks_the_caret_and_breathes_the_sign()
    {
        var f = new WordFrame();
        bool caretOff = false, signDipped = false;
        for (double t = 0; t <= 4; t += 0.01)
        {
            WordStyles.Fill(WordStyle.Type, 6, t, 4, WordMotion.Reduced, f);
            if (t < 3.2) Assert.Equal(1, f.CursorAlpha);
            WordStyles.Fill(WordStyle.Marquee, 6, t, 4, WordMotion.Reduced, f);
            if (t > 1.2) Assert.Equal(1, f.Letters[0].Lit, 3);
            WordStyles.Fill(WordStyle.Type, 6, t, 4, WordMotion.Full, f);
            if (t < 3.2 && f.CursorAlpha == 0) caretOff = true;
            WordStyles.Fill(WordStyle.Marquee, 6, t, 4, WordMotion.Full, f);
            if (t > 1.0 && f.Letters[0].Lit < 0.8) signDipped = true;
        }
        Assert.True(caretOff); Assert.True(signDipped);
    }

    [Theory]
    [InlineData(WordMotion.Full)]
    [InlineData(WordMotion.Reduced)]
    public void The_marquee_never_strobes(WordMotion motion)
    {
        // A bulb lights once and the sign breathes once: per letter at most one rise, one dip and
        // one rise back across the whole life, and the dip is slow (no frame-to-frame drop).
        var f = new WordFrame();
        for (int i = 0; i < 8; i++)
        {
            int turns = 0, dir = 0; double last = -1;
            for (double t = 0; t <= 4; t += 1 / 60.0)
            {
                WordStyles.Fill(WordStyle.Marquee, 8, t, 4, motion, f);
                double a = f.Letters[i].Lit;
                if (last >= 0)
                {
                    int d = Math.Sign(Math.Round(a - last, 6));
                    if (d != 0 && d != dir) { turns++; dir = d; }
                    if (d < 0) Assert.True(last - a < 0.05, $"letter {i} drops {last - a} in a frame at {t}");
                }
                last = a;
            }
            Assert.True(turns <= 3, $"letter {i} turned {turns} times");
        }
    }

    [Fact]
    public void Echo_and_drift_widen_the_ocr_skip_rect()
    {
        Assert.Equal((1.0, 0.0), WordStyles.Reach(WordStyle.Classic));
        Assert.True(WordStyles.Reach(WordStyle.Echo).Scale >= 1.4);
        Assert.True(WordStyles.Reach(WordStyle.Drift).RiseEm >= 0.55);
    }

    [Fact]
    public void Reduced_drift_travels_half_as_far()
    {
        var full = new WordFrame(); var half = new WordFrame();
        WordStyles.Fill(WordStyle.Drift, 4, 0.05, 4, WordMotion.Full, full);
        WordStyles.Fill(WordStyle.Drift, 4, 0.05, 4, WordMotion.Reduced, half);
        Assert.True(full.Letters[0].Dy > 0.1);
        Assert.Equal(full.Letters[0].Dy / 2, half.Letters[0].Dy, 6);
    }

    [Fact]
    public void A_frame_key_moves_with_the_frame_and_holds_when_it_holds()
    {
        var f = new WordFrame();
        WordStyles.Fill(WordStyle.Chalk, 5, 0.2, 4, WordMotion.Full, f); long a = f.Key();
        WordStyles.Fill(WordStyle.Chalk, 5, 0.4, 4, WordMotion.Full, f); long b = f.Key();
        WordStyles.Fill(WordStyle.Chalk, 5, 2.0, 4, WordMotion.Full, f); long c = f.Key();
        WordStyles.Fill(WordStyle.Chalk, 5, 2.5, 4, WordMotion.Full, f); long d = f.Key();
        Assert.NotEqual(a, b); Assert.Equal(c, d);
    }

    [Fact]
    public void The_marquee_is_in_capitals_and_the_others_keep_the_text()
    {
        Assert.Equal("SINK DEEPER", WordStyles.TextFor(WordStyle.Marquee, "sink deeper"));
        foreach (var s in new[] { WordStyle.Classic, WordStyle.Type, WordStyle.Chalk, WordStyle.Drift, WordStyle.Echo })
            Assert.Equal("sink deeper", WordStyles.TextFor(s, "sink deeper"));
        Assert.Equal(3, WordStyles.LetterCount("éab"));
    }

    [Fact]
    public void The_worn_style_is_read_once_and_again_after_the_word_slot_changes()
    {
        try
        {
            WornWordStyle.Read = _ => "fx.word.chalk";
            WornLooks.Raise(WornLooks.Word);
            Assert.Equal(WordStyle.Chalk, WornWordStyle.Current);
            WornWordStyle.Read = _ => "fx.word.type";
            WornLooks.Raise(WornLooks.Fog);           // another slot: the word keeps its style
            Assert.Equal(WordStyle.Chalk, WornWordStyle.Current);
            WornLooks.Raise(WornLooks.Word);
            Assert.Equal(WordStyle.Type, WornWordStyle.Current);
        }
        finally
        {
            WornWordStyle.Read = WornLooks.Get;
            WornLooks.Raise(WornLooks.Word);
        }
    }

    [Fact]
    public void Motion_off_is_still_and_reduced_is_half()
    {
        Assert.Equal(WordMotion.Still, WornWordStyle.MotionOf(ConditioningControlPanel.Models.MotionLevel.Off));
        Assert.Equal(WordMotion.Reduced, WornWordStyle.MotionOf(ConditioningControlPanel.Models.MotionLevel.Reduced));
        Assert.Equal(WordMotion.Full, WornWordStyle.MotionOf(ConditioningControlPanel.Models.MotionLevel.Full));
    }
}
