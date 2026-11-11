using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VaultMoteTests
{
    [Theory]
    [InlineData(MotionLevel.Full, 60, 64, 26, 1)]
    [InlineData(MotionLevel.Reduced, 60, 14, 4, 0.5)]
    [InlineData(MotionLevel.Off, 60, 0, 0, 0.5)]
    [InlineData(MotionLevel.Full, 8, 16, 26, 1)]
    [InlineData(MotionLevel.Full, 0, 0, 26, 1)]
    public void MotionAndGovernorLimitTheRoom(MotionLevel level, int budget, int target, double rate, double speed)
    {
        Assert.Equal(target, VaultMoteRules.Target(level, budget));
        Assert.Equal(rate, VaultMoteRules.SpawnPerSecond(level));
        Assert.Equal(speed, VaultMoteRules.SpeedScale(level));
    }

    [Theory]
    [InlineData(0.1, 0, 110, 24)]
    [InlineData(0.1, 1, 110, 8)]
    [InlineData(0.5, 1, 110, 72)]
    [InlineData(0.85, 1, -2, 40)]
    [InlineData(0.95, 1, 222, 40)]
    public void BirthsFollowAllFourEdgesWithinTheNarrowRing(double side, double depth, double x, double y)
    {
        var p = VaultMoteRules.SpawnOnRing(10, 20, 200, 40, side, 0.5, depth);
        Assert.Equal(x, p.X, 8);
        Assert.Equal(y, p.Y, 8);
    }
}
