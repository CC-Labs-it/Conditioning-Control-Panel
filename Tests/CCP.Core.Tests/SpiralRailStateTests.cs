using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SpiralRailStateTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void EveryGateMustAllowTheRail(bool enabled, bool hasBlock, bool withheld, bool expected)
        => Assert.Equal(expected, SpiralRailState.Visible(enabled, hasBlock, withheld));

    [Theory]
    [InlineData(-1, "·")]
    [InlineData(0, "·")]
    [InlineData(1, "I")]
    [InlineData(2, "II")]
    [InlineData(3, "III")]
    [InlineData(4, "IV")]
    [InlineData(5, "V")]
    [InlineData(6, "VI")]
    [InlineData(7, "VII")]
    [InlineData(8, "VIII")]
    [InlineData(9, "9")]
    [InlineData(123, "123")]
    public void NativeMedallionNumeralMatchesWpf(int stage, string numeral)
        => Assert.Equal(numeral, SpiralRailState.StageNumeral(stage));
}
