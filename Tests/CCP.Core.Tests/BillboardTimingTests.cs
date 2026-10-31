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
}
