using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BillboardTimingTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.35, 1.0625)] // cubic parameter t=0.5 gives x=.35, y=1.0625
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void ThudRetainsTheWpfOvershoot(double time, double expected) =>
        Assert.Equal(expected, BillboardTiming.Thud(time), 6);

    [Theory]
    [InlineData(0.5, 0.25, 0.1, 0.25, 1, 0.8024033876954126)]
    [InlineData(0.00001, 0, 0, 0, 1, 0.0013661362048752275)] // near-zero slope: bisection
    [InlineData(0.35, 0.2, 1.5, 0.4, 1, 1.0625000048521318)]
    [InlineData(0.5, 0.42, 0, 0.58, 1, 0.5)]
    public void EaseMatchesWpf(double x, double x1, double y1, double x2, double y2, double expected) =>
        Assert.Equal(expected, BillboardTiming.Ease(x, x1, y1, x2, y2), 7);
}
