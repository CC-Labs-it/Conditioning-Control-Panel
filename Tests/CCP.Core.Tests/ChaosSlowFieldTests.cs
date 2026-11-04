using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosSlowFieldTests
{
    [Fact]
    public void Chaos_slow_scales_motion_and_fuse_without_slowing_ambient()
    {
        var spec = new EffectBubbleSpec { IsLive = true, FuseMs = 5000, SizePx = 100 };
        var normal = AmbientBubble.SpawnChaos(new Random(42), 0, 0, 0, 1000, 1000, 1, spec);
        var slow = AmbientBubble.SpawnChaos(new Random(42), 0, 0, 0, 1000, 1000, 1, spec);
        double startY = normal.Y;
        normal.Step();
        slow.Step(timeScale: 0.12);
        Assert.Equal((startY - normal.Y) * 0.12, startY - slow.Y, 8);
        Assert.Equal(5000 - 32 * 0.12, slow.FuseRemainingMs, 8);
        var field = new AmbientBubbleField { ChaosTimeScale = 0.12 };
        field.Bubbles.Add(slow);
        field.Step();
        Assert.Equal(5000 - 64 * 0.12, slow.FuseRemainingMs, 8);
    }
}
