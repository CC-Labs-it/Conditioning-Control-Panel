using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EdgeFogMathTests
{
    [Fact]
    public void TargetsFollowTierAndReducedMotion()
    {
        Assert.Equal(5, EdgeFogMath.Target(EdgeFogMath.BigLong, 60, false));
        Assert.Equal(3, EdgeFogMath.Target(EdgeFogMath.BigLong, 24, false));
        Assert.Equal(2, EdgeFogMath.Target(EdgeFogMath.BigLong, 60, true));
        Assert.Equal(2, EdgeFogMath.Target(EdgeFogMath.BigLong, 24, true));
        Assert.Equal(0, EdgeFogMath.Target(EdgeFogMath.BigLong, 0, false));
        Assert.Equal(0.5, EdgeFogMath.ReducedSpeed);
        Assert.Equal(56, EdgeFogMath.StripPx);
    }

    [Fact]
    public void PositionsRunClockwiseInsideEachStrip()
    {
        Assert.Equal((30d, 7d), EdgeFogMath.Position(EdgeSide.Top, 30, 7, 200, 56));
        Assert.Equal((49d, 30d), EdgeFogMath.Position(EdgeSide.Right, 30, 7, 56, 200));
        Assert.Equal((170d, 49d), EdgeFogMath.Position(EdgeSide.Bottom, 30, 7, 200, 56));
        Assert.Equal((7d, 170d), EdgeFogMath.Position(EdgeSide.Left, 30, 7, 56, 200));
        Assert.Equal(200, EdgeFogMath.Length(EdgeSide.Right, 56, 200));
        Assert.Equal(200, EdgeFogMath.Length(EdgeSide.Top, 200, 56));
    }

    [Fact]
    public void FogFadesAndStaysWithinAuthoredOpacityAndDepth()
    {
        Assert.Equal(0, EdgeFogMath.Alpha(0.2, 0, 10, 1));
        Assert.Equal(0.2, EdgeFogMath.Alpha(0.2, 5, 10, 1), 6);
        Assert.Equal(0.22, EdgeFogMath.Alpha(1, 5, 10, 1.5), 6);
        Assert.Equal(0, EdgeFogMath.Alpha(0.2, 10, 10, 1));
        Assert.Equal(16, EdgeFogMath.MaxDepth(60, 8));
        Assert.Equal(15, EdgeFogMath.Advance(10, 5, 1));
        Assert.True(EdgeFogMath.IsSpent(10, 10, 50, 100, 10));
    }
}
