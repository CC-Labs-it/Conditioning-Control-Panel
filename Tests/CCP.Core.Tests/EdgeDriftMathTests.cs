using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EdgeDriftMathTests
{
    [Fact]
    public void BudgetLimitsEachStrip()
    {
        Assert.Equal(0, EdgeDriftMath.Target(0));
        Assert.Equal(3, EdgeDriftMath.Target(10));
        Assert.Equal(6, EdgeDriftMath.Target(60));
    }

    [Fact]
    public void PositionsAndMovementFollowClockwiseEdges()
    {
        Assert.Equal((0.25, 0.2), EdgeDriftMath.Position(EdgeSide.Top, 0.25, 0.2));
        Assert.Equal((0.8, 0.25), EdgeDriftMath.Position(EdgeSide.Right, 0.25, 0.2));
        Assert.Equal((0.75, 0.8), EdgeDriftMath.Position(EdgeSide.Bottom, 0.25, 0.2));
        Assert.Equal((0.2, 0.75), EdgeDriftMath.Position(EdgeSide.Left, 0.25, 0.2));
        Assert.Equal(0.3, EdgeDriftMath.Advance(0.2, 0.05, 2), 6);
        Assert.True(EdgeDriftMath.IsSpent(1, 5));
        Assert.True(EdgeDriftMath.IsSpent(0.5, 0));
    }

    [Fact]
    public void AlphaFadesAtEndsAndLifetimeBoundaries()
    {
        Assert.Equal(0, EdgeDriftMath.Alpha(0, 5, 10, 0, 1));
        Assert.Equal(0, EdgeDriftMath.Alpha(0.5, 10, 10, 0, 1));
        Assert.Equal(0.2775, EdgeDriftMath.Alpha(0.5, 5, 10, 0, 1), 6);
        Assert.Equal(0, EdgeDriftMath.Alpha(0.5, 5, 0, 0, 1));
    }
}
