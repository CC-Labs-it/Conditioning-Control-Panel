using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FxBurstAnchorRulesTests
{
    [Theory]
    [InlineData(FxBurstSpot.Center, 290, 304)]
    [InlineData(FxBurstSpot.RightEdge, 540, 304)]
    [InlineData(FxBurstSpot.LeftEdge, 40, 304)]
    [InlineData(FxBurstSpot.TopCenter, 290, 300)]
    [InlineData(FxBurstSpot.BottomCenter, 290, 308)]
    public void BurstLandsOnTheRequestedPartOfTheMappedAnchor(FxBurstSpot spot, double x, double y)
    {
        Assert.True(FxBurstAnchorRules.TryResolve((40, 300, 500, 8), (1600, 900), spot, out var origin));
        Assert.Equal((x, y), origin);
    }

    [Theory]
    [InlineData(200, -60, 300, 40)]
    [InlineData(-400, 100, 300, 40)]
    [InlineData(1700, 100, 300, 40)]
    [InlineData(200, 1000, 300, 40)]
    [InlineData(10, 10, 0, 0)]
    [InlineData(double.NaN, 10, 40, 40)]
    [InlineData(10, double.PositiveInfinity, 40, 40)]
    public void OffScreenOrUnmeasuredAnchorsEmitNothing(double x, double y, double width, double height)
        => Assert.False(FxBurstAnchorRules.TryResolve((x, y, width, height), (1600, 900), FxBurstSpot.Center, out _));

    [Fact]
    public void ClippedAnchorIsClampedIntoTheHostAndUnmeasuredHostRefuses()
    {
        Assert.True(FxBurstAnchorRules.TryResolve((1580, 880, 80, 80), (1600, 900), FxBurstSpot.BottomCenter, out var origin));
        Assert.Equal((1600d, 900d), origin);
        Assert.False(FxBurstAnchorRules.TryResolve((10, 10, 40, 40), (0, 0), FxBurstSpot.Center, out _));
        Assert.False(FxBurstAnchorRules.TryResolve((10, 10, 40, 40), (double.NaN, 900), FxBurstSpot.Center, out _));
    }
}
