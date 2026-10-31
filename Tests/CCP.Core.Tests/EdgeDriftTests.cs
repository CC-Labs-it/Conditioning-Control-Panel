using System;
using ConditioningControlPanel.Services.UI;
using Xunit;
namespace CCP.Core.Tests;
public class EdgeDriftTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(10, 3)] [InlineData(24, 6)] [InlineData(60, 6)]
    public void BudgetIsSharedAndCapped(int budget, int target) => Assert.Equal(target, EdgeDriftMath.Target(budget));

    [Theory]
    [InlineData(EdgeSide.Top, 1, 0)] [InlineData(EdgeSide.Right, 0, 1)]
    [InlineData(EdgeSide.Bottom, -1, 0)] [InlineData(EdgeSide.Left, 0, -1)]
    public void MotionIsClockwiseAndDepthIsFixed(EdgeSide side, int dx, int dy)
    {
        var a = EdgeDriftMath.Position(side, .3, .5);
        var b = EdgeDriftMath.Position(side, EdgeDriftMath.Advance(.3, .02, 1), .5);
        Assert.Equal(dx, Math.Sign(Math.Round(b.X - a.X, 6)));
        Assert.Equal(dy, Math.Sign(Math.Round(b.Y - a.Y, 6)));
        Assert.Equal(.3, EdgeDriftMath.Advance(.3, .02, -1));
    }

    [Fact]
    public void LifeFlickerAndEndsControlAlpha()
    {
        Assert.Equal(.30, EdgeDriftMath.Alpha(.5, 5, 10, Math.PI / 2, 1), 6);
        Assert.Equal(.255, EdgeDriftMath.Alpha(.5, 5, 10, -Math.PI / 2, 1), 6);
        Assert.Equal(0, EdgeDriftMath.Alpha(.5, 10, 10, 0, 1), 6);
        Assert.Equal(0, EdgeDriftMath.Alpha(1, 5, 10, 0, 1), 6);
        Assert.True(EdgeDriftMath.IsSpent(1, 3));
        Assert.True(EdgeDriftMath.IsSpent(.4, 0));
        Assert.False(EdgeDriftMath.IsSpent(.4, 3));
    }
}
