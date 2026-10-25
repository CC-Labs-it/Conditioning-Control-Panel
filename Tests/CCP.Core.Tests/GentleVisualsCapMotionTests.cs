using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The motion cap both heads share (WPF MotionFx.ResolveLevel, Avalonia AmbientFxCanvas.Env.Level).</summary>
public class GentleVisualsCapMotionTests
{
    [Theory]
    [InlineData(MotionLevel.Full, true, false, MotionLevel.Full)]
    [InlineData(MotionLevel.Full, true, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Full, false, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, true, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, true, true, MotionLevel.Off)]
    [InlineData(MotionLevel.Off, false, false, MotionLevel.Off)]
    public void CapMotionOnlyEverRemovesMotion(MotionLevel setting, bool osAnim, bool gentle, MotionLevel expected)
        => Assert.Equal(expected, GentleVisuals.CapMotion(setting, osAnim, gentle));
}
