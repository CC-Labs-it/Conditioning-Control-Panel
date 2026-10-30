using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF AmbientBubbleMotion + SpiralInPath + the BubbleService ambient Rain / Spiral In spawn and tick.
public sealed class BubbleMotionRollTests
{
    [Fact]
    public void Resolve_honours_grants_motion_level_and_mix_weights()
    {
        Assert.Equal(BubbleMotionStyle.FloatUp, BubbleMotionRoll.Resolve(BubbleMotionStyle.Rain, false, true, MotionLevel.Full, 0));
        Assert.Equal(BubbleMotionStyle.Rain, BubbleMotionRoll.Resolve(BubbleMotionStyle.Rain, true, false, MotionLevel.Full, 0));
        Assert.Equal(BubbleMotionStyle.FloatUp, BubbleMotionRoll.Resolve(BubbleMotionStyle.SpiralIn, true, true, MotionLevel.Off, 0));
        // Mix with only Spiral In owned: two equal buckets, FloatUp then SpiralIn.
        Assert.Equal(BubbleMotionStyle.FloatUp, BubbleMotionRoll.Resolve(BubbleMotionStyle.Mix, false, true, MotionLevel.Full, 0.2));
        Assert.Equal(BubbleMotionStyle.SpiralIn, BubbleMotionRoll.Resolve(BubbleMotionStyle.Mix, false, true, MotionLevel.Full, 0.7));
        Assert.Equal(BubbleMotionStyle.Rain, BubbleMotionRoll.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 0.5));
        Assert.Equal(BubbleMotionStyle.SpiralIn, BubbleMotionRoll.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 1.0));
        Assert.False(BubbleMotionRoll.IsOwned(BubbleMotionStyle.Mix, false, false));
        Assert.Equal(0.5, BubbleMotionRoll.SpeedMult(MotionLevel.Reduced));
    }

    [Fact]
    public void Rain_starts_above_the_top_falls_and_misses_below_the_bottom()
    {
        var s = new AppSettings();
        var b = AmbientBubble.Spawn(new Random(1), 0, 0, 0, 1920, 1080, 1, s, null, true, BubbleMotionStyle.Rain);
        Assert.Equal(-b.Size, b.Y);
        var y = b.Y;
        Assert.Equal(AmbientBubble.Result.Alive, b.Step());
        Assert.InRange(b.Y - y, 1.0, 2.0);                     // falls 1..2 DIP a step
        var steps = 0;
        while (b.Step() == AmbientBubble.Result.Alive && steps < 5000) steps++;
        Assert.True(b.Y > 1080 + 50);
    }

    [Fact]
    public void Spiral_in_walks_a_ring_inward_fades_and_ends_at_the_core_as_a_miss()
    {
        var s = new AppSettings();
        var b = AmbientBubble.Spawn(new Random(2), 0, 0, 0, 1920, 1080, 1, s, null, true, BubbleMotionStyle.SpiralIn);
        double R(AmbientBubble x) => Math.Sqrt(Math.Pow(x.CenterX - 960, 2) + Math.Pow(x.CenterY - 540, 2));
        Assert.Equal(1080 * BubbleSpiralPath.StartRadiusFrac, R(b), 6);
        var r0 = R(b);
        b.Step();
        Assert.True(R(b) < r0);
        Assert.Equal(1.0, b.MotionFade);
        var result = AmbientBubble.Result.Alive;
        var steps = 0;
        while (result == AmbientBubble.Result.Alive && steps++ < 5000) result = b.Step();
        Assert.Equal(AmbientBubble.Result.Missed, result);
        Assert.Equal(0.0, b.MotionFade);
        Assert.Equal(BubbleSpiralPath.CoreDip, R(b), 6);
    }
}
