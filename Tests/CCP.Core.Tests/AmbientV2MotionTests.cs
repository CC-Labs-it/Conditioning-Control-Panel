using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AmbientV2MotionTests
{
    [Theory]
    [InlineData(BubbleMotionStyle.Rain)]
    [InlineData(BubbleMotionStyle.SpiralIn)]
    public void Owned_motion_travels_to_exit_and_never_leaves_opacity_range(BubbleMotionStyle style)
    {
        var s = new AppSettings { BubbleMotionStyle = style, MotionLevel = MotionLevel.Full };
        var bubble = AmbientBubble.Spawn(new Random(7), 0, 100, 50, 1920, 1080, 1.5, s, null, true, true, true);
        var y = bubble.Y;
        var result = bubble.Step();
        if (style == BubbleMotionStyle.Rain) Assert.True(bubble.Y > y);
        int steps = 0;
        while (result == AmbientBubble.Result.Alive && steps++ < 20000)
        {
            result = bubble.Step();
            Assert.InRange(bubble.Fade, 0, 1);
        }
        Assert.Equal(AmbientBubble.Result.Missed, result);
    }

    [Theory]
    [InlineData(false, MotionLevel.Full)]
    [InlineData(true, MotionLevel.Off)]
    public void Unowned_or_motion_off_remains_float_up(bool owned, MotionLevel level)
    {
        var s = new AppSettings { BubbleMotionStyle = BubbleMotionStyle.Rain, MotionLevel = level };
        var bubble = AmbientBubble.Spawn(new Random(7), 0, 0, 0, 1920, 1080, 1, s, null, true, owned, owned);
        var y = bubble.Y;
        bubble.Step();
        Assert.True(bubble.Y < y);
    }

    [Fact]
    public void Mix_only_chooses_owned_styles_and_reduced_motion_halves_speed_and_turns()
    {
        Assert.Equal(BubbleMotionStyle.Rain, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, true, false, MotionLevel.Full, .9));
        Assert.Equal(BubbleMotionStyle.SpiralIn, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, false, true, MotionLevel.Full, .9));
        Assert.Equal(BubbleMotionStyle.FloatUp, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, false, false, MotionLevel.Full, .9));
        Assert.Equal(.5, AmbientBubbleMotionRules.SpeedMult(MotionLevel.Reduced));
        Assert.Equal(AmbientSpiralInPath.FullTurns / 2, AmbientSpiralInPath.Turns(MotionLevel.Reduced));
    }
}
