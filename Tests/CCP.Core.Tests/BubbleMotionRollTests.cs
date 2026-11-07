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
        var b = AmbientBubble.Spawn(new Random(1), 0, 0, 0, 1920, 1080, 1, s, null, true, MotionLevel.Full, BubbleMotionStyle.Rain);
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
        var b = AmbientBubble.Spawn(new Random(2), 0, 0, 0, 1920, 1080, 1, s, null, true, MotionLevel.Full, BubbleMotionStyle.SpiralIn);
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
    [Fact]
    public void Effective_reduced_level_halves_rain_speed_and_spiral_turns()
    {
        // WPF MotionFx.cs:36-53; BubbleService.cs:3316,3373. Raw settings stay Full.
        var settings = new AppSettings { MotionLevel = MotionLevel.Full };
        AmbientBubble Spawn(BubbleMotionStyle motion, MotionLevel level) =>
            AmbientBubble.Spawn(new Random(2), 0, 0, 0, 1920, 1080, 1, settings, null, true, level, motion);
        double Fall(MotionLevel level)
        {
            var b = Spawn(BubbleMotionStyle.Rain, level); var y = b.Y;
            b.Step(); return b.Y - y;
        }
        Assert.Equal(Fall(MotionLevel.Full) / 2, Fall(MotionLevel.Reduced), 8);
        double Turns(MotionLevel level)
        {
            var b = Spawn(BubbleMotionStyle.SpiralIn, level);
            System.Numerics.Complex Point() => new(b.CenterX - 960, b.CenterY - 540);
            var previous = Point(); var angle = 0d;
            for (var i = 0; i < 5000; i++)
            {
                var result = b.Step(); var next = Point();
                angle += (next / previous).Phase; previous = next;
                if (result == AmbientBubble.Result.Missed) return Math.Abs(angle / (2 * Math.PI));
            }
            throw new InvalidOperationException("Spiral did not finish");
        }
        Assert.InRange(Turns(MotionLevel.Full), 2.5, 2.51);
        Assert.InRange(Turns(MotionLevel.Reduced), 1.25, 1.26);
    }

}
