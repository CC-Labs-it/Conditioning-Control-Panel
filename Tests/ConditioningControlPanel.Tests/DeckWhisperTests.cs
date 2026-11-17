using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Shop looks on the Deck: "words behind the card" (slot <c>behind</c>). The default row keeps the card
/// picture to picture; worn, the back is a spiral with a phrase (DeckSpiralBackTests, DeckCardShotTests).
/// </summary>
public class DeckWhisperTests
{
    [Fact]
    public void Only_the_whisper_row_gives_the_card_a_spiral_back()
    {
        Assert.False(DeckWhisper.On(WornLooks.DefaultOf(WornLooks.Behind)));
        Assert.False(DeckWhisper.On(null));
        Assert.False(DeckWhisper.On("fx.card.ink"));
        Assert.True(DeckWhisper.On("fx.behind.whisper"));
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
    public void A_back_takes_whole_phrases_in_the_player_s_own_case()
    {
        var rng = new Random(5);
        var pool = new Dictionary<string, bool> { ["Good  Girls Obey"] = true, ["x".PadRight(41, 'x')] = true };
        for (int i = 0; i < 10; i++)
            Assert.Equal("Good Girls Obey", DeckWhisper.Pick(pool, null, rng, DeckSpiralBack.MaxChars, lower: false));
    }
}
