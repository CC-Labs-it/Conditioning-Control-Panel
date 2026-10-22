using System;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public sealed class PinkRushWashRulesTests
{
    [Theory]
    [InlineData(-100, 60)]
    [InlineData(0, 60)]
    [InlineData(250, 30)]
    [InlineData(500, 0)]
    [InlineData(9000, 0)]
    public void WpfWashStrengthAndLinearFadeArePreserved(double elapsed, int alpha)
        => Assert.Equal(alpha, PinkRushRules.WashAlphaAt(elapsed, PinkRushRules.WashFadeMs, PinkRushRules.WashPeak));

    [Fact]
    public void InvalidInputAndPeakAreBounded()
    {
        Assert.Equal(0, PinkRushRules.WashAlphaAt(double.NaN, 500, 1));
        Assert.Equal(0, PinkRushRules.WashAlphaAt(0, 500, double.NaN));
        Assert.Equal(0, PinkRushRules.WashAlphaAt(0, 0, 1));
        Assert.Equal(0, PinkRushRules.WashAlphaAt(0, 500, -2));
        Assert.Equal(255, PinkRushRules.WashAlphaAt(0, 500, 2));
    }
}
