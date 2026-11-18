using ConditioningControlPanel.BubbleCore;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The worn looks seam: defaults with nobody feeding it, bubble rows to skin ids, and the
/// DEBUG env switch beating the worn skin (lane I).</summary>
public class WornLooksTests
{
    [Fact]
    public void With_nobody_feeding_it_every_slot_reads_today_s_look()
    {
        Assert.Equal("fx.bubble.soap", WornLooks.DefaultOf(WornLooks.Bubble));
        Assert.Equal("fx.card.plain", WornLooks.DefaultOf(WornLooks.Card));
        Assert.Equal("fx.behind.none", WornLooks.DefaultOf(WornLooks.Behind));
        Assert.Equal("fx.fog.pink", WornLooks.DefaultOf(WornLooks.Fog));
        Assert.Equal("fx.fog.lilac", WornLooks.DefaultOf(WornLooks.Fog2));
        Assert.Equal("fx.word.classic", WornLooks.DefaultOf(WornLooks.Word));
        Assert.Null(WornLooks.DefaultOf(WornLooks.Mix));
        Assert.Equal(BubbleSkinIds.Soap, WornLooks.SkinOf(WornLooks.DefaultOf(WornLooks.Bubble)));
    }

    [Theory]
    [InlineData("fx.bubble.champagne", "champagne")]
    [InlineData("fx.bubble.heart", "heart")]
    [InlineData("fx.bubble.", "soap")]
    [InlineData("fx.card.ink", "soap")]
    [InlineData(null, "soap")]
    public void A_bubble_row_names_its_skin(string? row, string skin) => Assert.Equal(skin, WornLooks.SkinOf(row));

    [Fact]
    public void The_env_switch_beats_the_worn_skin_and_undrawn_skins_stay_soap()
    {
        Assert.Equal(BubbleSkinIds.Champagne, BubbleSkinChoice.Choose(null, "champagne"));
        Assert.Equal(BubbleSkinIds.Soap, BubbleSkinChoice.Choose("soap", "champagne"));
        Assert.Equal(BubbleSkinIds.Champagne, BubbleSkinChoice.Choose(" Champagne ", null));
        Assert.Equal(BubbleSkinIds.Soap, BubbleSkinChoice.Choose("  ", "bogus"));
        Assert.Equal(BubbleSkinIds.Soap, BubbleSkinChoice.Choose(null, null));
    }
}
