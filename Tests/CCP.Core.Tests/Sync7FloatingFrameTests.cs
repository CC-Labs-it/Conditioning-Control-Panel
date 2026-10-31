using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace CCP.Core.Tests;

public sealed class Sync7FloatingFrameTests
{
    [Fact]
    public void ThrowBouncesAndStaysOnScreenAndOffPlaces()
    {
        var m = new FloatingVideoMotionModel(100, 100, 200, 120, 0, 0, 800, 600);
        m.Press(110, 110, 0);
        m.Move(210, 110, 40);
        m.Step(.04);
        m.Release(40, MotionLevel.Full);
        Assert.True(m.Flying);
        Assert.True(m.Vx > 0);
        var bounced = false;
        for (var i = 0; i < 200; i++)
        {
            m.Step(.02);
            bounced |= m.Vx < 0;
            Assert.InRange(m.X, 0, 600);
            Assert.InRange(m.Y, 0, 480);
        }
        Assert.True(bounced);
        m.Press(m.X + 10, m.Y + 10, 5000);
        m.Move(m.X + 60, m.Y + 10, 5040);
        m.Release(5040, MotionLevel.Off);
        Assert.False(m.Flying);
    }

    [Fact]
    public void AudioLightEasesBreathesAndRespectsMotion()
    {
        Assert.Equal(.5, FloatingFrameLook.Approach(0, true, .09), 5);
        Assert.Equal(.5, FloatingFrameLook.Approach(1, false, .16), 5);
        Assert.Equal(0, FloatingFrameLook.Breath(1.2, MotionLevel.Off));
        Assert.Equal(.5, FloatingFrameLook.Breath(1.2, MotionLevel.Reduced), 5);
        var quiet = FloatingFrameLook.Border(255, 105, 180, 0, 0);
        var lit = FloatingFrameLook.Border(255, 105, 180, 1, 0);
        var peak = FloatingFrameLook.Border(255, 105, 180, 1, 1);
        Assert.True(quiet.G < lit.G && lit.G < peak.G);
    }
}
