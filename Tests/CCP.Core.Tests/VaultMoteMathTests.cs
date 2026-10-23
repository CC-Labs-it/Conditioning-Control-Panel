using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VaultMoteMathTests
{
    [Theory]
    [InlineData(MotionLevel.Full, 64, 26, 1)]
    [InlineData(MotionLevel.Reduced, 14, 4, 0.5)]
    [InlineData(MotionLevel.Off, 0, 0, 0.5)]
    public void MotionAndPerformanceBoundTheRoom(MotionLevel level, int cap, double rate, double speed)
    {
        Assert.Equal(cap, VaultMoteMath.Target(level, 60));
        Assert.Equal(0, VaultMoteMath.Target(level, 0));
        Assert.InRange(VaultMoteMath.Target(level, 3), 0, 6);
        Assert.Equal(rate, VaultMoteMath.SpawnPerSecond(level));
        Assert.Equal(speed, VaultMoteMath.SpeedScale(level));
    }

    [Fact]
    public void SpawnUsesAllFourEdgesAndTheAuthoredRingDepth()
    {
        var r = new RectD(10, 20, 100, 50);
        Assert.Equal(new Vec2(60, 8), VaultMoteMath.SpawnOnRing(r, 0, 0.5, 1));
        Assert.Equal(new Vec2(60, 82), VaultMoteMath.SpawnOnRing(r, 0.4, 0.5, 1));
        Assert.Equal(new Vec2(-2, 45), VaultMoteMath.SpawnOnRing(r, 0.7, 0.5, 1));
        Assert.Equal(new Vec2(122, 45), VaultMoteMath.SpawnOnRing(r, 0.9, 0.5, 1));
        Assert.Equal(new Vec2(60, 24), VaultMoteMath.SpawnOnRing(r, 0, 0.5, 0));
    }
}
