using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Magnet bubble rules against the WPF variant table (ChaosBubbleVariants is still
/// WPF-only; the rest of MagnetBubbleTests moved to CCP.Core.Tests with MagnetBubble).</summary>
public class MagnetBubbleVariantsTests
{
    /// <summary>
    /// No sprite ships for it, so the body glyph is all the user has to tell it apart at a glance.
    /// It must not be one another bubble already wears - the chaos "spiral" variant's U+25CE was
    /// the original pick and collided exactly there.
    /// </summary>
    [Fact]
    public void Its_body_glyph_is_not_already_taken()
    {
        Assert.False(string.IsNullOrEmpty(MagnetBubble.Label));
        foreach (var v in ChaosBubbleVariants.All)
            Assert.NotEqual(v.Label, MagnetBubble.Label);
        Assert.NotEqual("◎", MagnetBubble.Label);   // the spiral variant
        Assert.NotEqual("◍", MagnetBubble.Label);   // the Brain Drain bubble
    }

    /// <summary>The floating pop word. Its pop reaches ShowChaosEffectLabel like any other, so a
    /// missing case would flash nothing where every neighbour flashes a token.</summary>
    [Fact]
    public void It_has_a_pop_word()
        => Assert.Equal("PULL", ChaosBubbleVariants.PopWordFor(MagnetBubble.VariantId));
}
