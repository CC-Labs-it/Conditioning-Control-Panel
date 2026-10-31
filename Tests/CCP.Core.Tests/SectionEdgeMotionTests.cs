using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SectionEdgeMotionTests
{
    [Theory]
    [InlineData(0, 1.5)]
    [InlineData(1, 4.5)]
    [InlineData(2, 7.5)]
    [InlineData(3, 10.5)]
    public void LiftVisitsEachSideClockwiseOncePerTwelveSecondLap(int side, double time)
    {
        Assert.Equal(0, SectionEdgeMotion.Position(side, time, MotionLevel.Full));
        Assert.Equal(0, SectionEdgeMotion.Position(side, time + 12, MotionLevel.Full));
        Assert.Equal(side < 2 ? -1 : 1, SectionEdgeMotion.Position(side, time - 1.5, MotionLevel.Full));
        Assert.Equal(side < 2 ? 1 : -1, SectionEdgeMotion.Position(side, time + 1.4, MotionLevel.Full) > 0 ? 1 : -1);
    }

    [Fact]
    public void ReducedParksTopCentreOffParksOutsideAndOnlyActiveFullRuns()
    {
        Assert.Equal(0, SectionEdgeMotion.Position(0, 4, MotionLevel.Reduced));
        for (int side = 1; side < 4; side++) Assert.Equal(-1, SectionEdgeMotion.Position(side, 4, MotionLevel.Reduced));
        for (int side = 0; side < 4; side++) Assert.Equal(-1, SectionEdgeMotion.Position(side, 4, MotionLevel.Off));
        Assert.True(SectionEdgeMotion.ShouldRun(MotionLevel.Full, true, true, false));
        Assert.False(SectionEdgeMotion.ShouldRun(MotionLevel.Full, false, true, false));
        Assert.False(SectionEdgeMotion.ShouldRun(MotionLevel.Full, true, false, false));
        Assert.False(SectionEdgeMotion.ShouldRun(MotionLevel.Full, true, true, true));
        Assert.False(SectionEdgeMotion.ShouldRun(MotionLevel.Reduced, true, true, false));
        Assert.False(SectionEdgeMotion.ShouldRun(MotionLevel.Off, true, true, false));
    }
    [Theory]
    [InlineData(MotionLevel.Full, true, MotionLevel.Full)]
    [InlineData(MotionLevel.Full, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, true, MotionLevel.Off)]
    [InlineData(MotionLevel.Off, false, MotionLevel.Off)]
    public void AmbientGateParksFullButNeverOverridesOff(MotionLevel level, bool ambient, MotionLevel expected) =>
        Assert.Equal(expected, SectionEdgeMotion.EffectiveLevel(level, ambient));

}
