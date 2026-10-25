using ConditioningControlPanel.Motion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionThinkingPulseTests
{
    [Theory]
    [InlineData(0, 0, .25)]
    [InlineData(450, 0, 1)]
    [InlineData(900, 0, .25)]
    [InlineData(0, 1, 1)]
    [InlineData(150, 1, .25)]
    [InlineData(600, 1, 1)]
    [InlineData(300, 2, .25)]
    [InlineData(750, 2, 1)]
    [InlineData(1050, 1, 1)]
    [InlineData(1200, 1, .25)]
    [InlineData(1200, 2, 1)]
    [InlineData(1500, 2, .25)]
    public void MatchesTheWpfAutoReverseAndStagger(double milliseconds, int dot, double expected)
        => Assert.Equal(expected, CompanionThinkingPulse.Opacity(milliseconds, dot), 8);

    [Fact]
    public void EveryFrameStaysInTheOpacityRange()
    {
        for (int ms = 0; ms < 5000; ms++)
            for (int dot = 0; dot < 3; dot++)
                Assert.InRange(CompanionThinkingPulse.Opacity(ms, dot), .25, 1);
        Assert.Equal(1, CompanionThinkingPulse.Opacity(double.NaN, 0));
    }
}
