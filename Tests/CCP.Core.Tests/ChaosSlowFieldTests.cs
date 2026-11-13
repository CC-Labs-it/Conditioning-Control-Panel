using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

// WPF BubbleService.cs:257, :2612, :4010-4230: the chaos slow-mo (hand-ported from #4303, restack b145).
public class ChaosSlowFieldTests
{
    private static AmbientBubble Live(ChaosMotion motion = ChaosMotion.FloatUp) =>
        AmbientBubble.SpawnChaos(new Random(42), 0, 0, 0, 1000, 1000, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000, SizePx = 100 }, motion);

    [Fact]
    public void Slow_motion_keeps_the_wobble_at_normal_animation_speed()
    {
        var normal = Live();
        var slow = Live();
        for (int i = 0; i < 20; i++)
        {
            normal.Step();
            slow.Step(timeScale: 0.12);
            Assert.Equal(normal.X, slow.X, 8);   // the sideways wobble is time-of-life, not travel
        }
    }

    [Theory]
    [InlineData(ChaosMotion.FloatUp)]
    [InlineData(ChaosMotion.RainDown)]
    public void Chaos_slow_scales_travel_and_fuse(ChaosMotion motion)
    {
        var normal = Live(motion);
        var slow = Live(motion);
        double startY = normal.Y;
        normal.Step();
        slow.Step(timeScale: 0.12);
        Assert.Equal((startY - normal.Y) * 0.12, startY - slow.Y, 8);
        Assert.Equal(5000 - AmbientBubble.FuseStepMs * 0.12, slow.FuseRemainingMs, 8);
    }

    [Fact]
    public void Field_time_scale_slows_chaos_only_and_clamps()
    {
        var field = new AmbientBubbleField { ChaosTimeScale = 0.12 };
        var live = Live();
        field.Bubbles.Add(live);
        field.Step();
        Assert.Equal(5000 - AmbientBubble.FuseStepMs * 0.12, live.FuseRemainingMs, 8);
        field.ChaosTimeScale = 0;
        Assert.Equal(0.05, field.ChaosTimeScale);
        field.ChaosTimeScale = 3;
        Assert.Equal(1.0, field.ChaosTimeScale);
    }
}
