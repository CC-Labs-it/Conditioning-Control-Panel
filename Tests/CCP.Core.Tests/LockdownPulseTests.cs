using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public class LockdownPulseTests
{
    [Fact]
    public void Track_matches_Wpf_endpoints_midpoint_and_auto_reverse()
    {
        Assert.Equal((255, 20, 147, 12d, .7), LockdownPulse.At(0));
        Assert.Equal((255, 105, 180, 22d, 1d), LockdownPulse.At(1.5));
        var midpoint = LockdownPulse.At(.75);
        Assert.Equal((byte)255, midpoint.R);
        Assert.Equal((byte)77, midpoint.G);
        Assert.Equal((byte)165, midpoint.B);
        Assert.Equal(17, midpoint.Blur);
        Assert.Equal(.85, midpoint.Opacity, 12);
        Assert.Equal(midpoint, LockdownPulse.At(2.25));
        Assert.Equal(LockdownPulse.At(0), LockdownPulse.At(3));
        Assert.Equal(midpoint, LockdownPulse.At(300.75));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_elapsed_time_stays_at_the_start(double seconds) =>
        Assert.Equal(LockdownPulse.At(0), LockdownPulse.At(seconds));
}
