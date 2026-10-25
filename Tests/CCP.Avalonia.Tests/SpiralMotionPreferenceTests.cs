using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralMotionPreferenceTests
{
    [Theory]
    [InlineData(MotionLevel.Full, false, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Full, true, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Full, true, false, MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced, true, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, false, true, MotionLevel.Off)]
    [InlineData(MotionLevel.Off, true, false, MotionLevel.Off)]
    public void OsAndGentleCapsCanOnlyRemoveMotion(MotionLevel setting, bool os, bool gentle, MotionLevel expected)
        => Assert.Equal(expected, UiMotionPreference.ResolveLevel(setting, os, gentle));

    [Fact]
    public void GentleVisualsCapsTheLiveAmbientGateWithoutRewritingTheSetting()
    {
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.GentleVisuals, s.PerformanceMode);
        try
        {
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            s.GentleVisuals = true;
            Assert.Equal(MotionLevel.Reduced, AmbientFxCanvas.Env.Level);
            Assert.False(AmbientFxCanvas.Env.AllowAmbientLoops);
            Assert.True(AmbientFxCanvas.Env.AllowTransitions);
            Assert.Equal(MotionLevel.Full, s.MotionLevel);
            s.MotionLevel = MotionLevel.Off;
            Assert.Equal(MotionLevel.Off, AmbientFxCanvas.Env.Level);
            Assert.False(AmbientFxCanvas.Env.AllowTransitions);
        }
        finally { (s.MotionLevel, s.GentleVisuals, s.PerformanceMode) = old; }
    }

    [Fact]
    public void OsAnimationFlagIsCachedForPerFrameReaders()
    {
        var oldProbe = UiMotionPreference.OsProbe;
        var calls = 0;
        try
        {
            UiMotionPreference.OsProbe = () => { System.Threading.Interlocked.Increment(ref calls); return true; };
            UiMotionPreference.CacheStamp = long.MinValue;
            for (var i = 0; i < 100; i++) Assert.True(UiMotionPreference.AnimationsEnabled);
            Assert.InRange(calls, 1, 3); // parallel readers may race the first probe; uncached would be 100
        }
        finally
        {
            UiMotionPreference.OsProbe = oldProbe;
            UiMotionPreference.CacheStamp = long.MinValue;
        }
    }
}
