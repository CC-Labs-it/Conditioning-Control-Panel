using ConditioningControlPanel.Models;
using ConditioningControlPanel.Motion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MotionPolicyTests
{
    [Theory]
    [InlineData(MotionLevel.Full, true, MotionLevel.Full)]
    [InlineData(MotionLevel.Full, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, true, MotionLevel.Off)]
    [InlineData(MotionLevel.Off, false, MotionLevel.Off)]
    public void OsPreferenceOnlyRemovesMotion(MotionLevel setting, bool enabled, MotionLevel expected)
        => Assert.Equal(expected, MotionPolicy.ResolveLevel(setting, enabled));
}
