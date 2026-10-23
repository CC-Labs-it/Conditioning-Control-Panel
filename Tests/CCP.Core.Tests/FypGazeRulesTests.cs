using System;
using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FypGazeRulesTests
{
    [Fact]
    public void MixedDpi_UsesBothMonitorScalesAndOrigins()
    {
        var g = FypGazeRules.Normalize(200, 200, 1920, 0, 1.5, 2020, 100, 2, 400, 300)!.Value;
        Assert.Equal(0.25, g.X, 6);
        Assert.Equal(1.0 / 3, g.Y, 6);
        Assert.True(g.Inside);
    }

    [Theory]
    [InlineData(-200, 100, 0, 0.5, false)]
    [InlineData(600, 300, 1, 1, false)]
    [InlineData(400, 200, 1, 1, true)]
    [InlineData(0, 0, 0, 0, true)]
    public void Outside_IsClampedButFlagged(double x, double y, double nx, double ny, bool inside) =>
        Assert.Equal((nx, ny, inside), FypGazeRules.Normalize(x, y, 0, 0, 1, 0, 0, 1, 400, 200)!.Value);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 100)]
    [InlineData(100, double.NaN)]
    public void UnlaidOutWebView_DoesNotPublish(double width, double height) =>
        Assert.Null(FypGazeRules.Normalize(1, 1, 0, 0, 1, 0, 0, 1, width, height));

    [Fact]
    public void InvalidScale_FallsBack_AndNonfiniteGazeIsDropped()
    {
        Assert.Equal((0.5, 0.5, true), FypGazeRules.Normalize(50, 50, 0, 0, double.NaN, 0, 0, 0, 100, 100)!.Value);
        Assert.Null(FypGazeRules.Normalize(double.PositiveInfinity, 1, 0, 0, 1, 0, 0, 1, 100, 100));
    }
}
