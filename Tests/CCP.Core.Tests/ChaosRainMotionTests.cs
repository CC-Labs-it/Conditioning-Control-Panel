using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosRainMotionTests
{
    [Fact]
    public void Rain_starts_above_the_screen_moves_down_and_exits_below()
    {
        var spec = ChaosBubbleVariants.Build(ChaosBubbleVariants.All[0], 0, motionOverride: ChaosMotion.RainDown);
        var bubble = AmbientBubble.SpawnChaos(new Random(1), 0, 100, 200, 800, 600, 2, spec);
        Assert.Equal(100 - spec.SizePx, bubble.Y, 6);
        double start = bubble.Y;
        Assert.Equal(AmbientBubble.Result.Alive, bubble.Step());
        Assert.True(bubble.Y > start);
        AmbientBubble.Result result = AmbientBubble.Result.Alive;
        for (int i = 0; i < 5000 && result == AmbientBubble.Result.Alive; i++) result = bubble.Step();
        Assert.Equal(AmbientBubble.Result.Missed, result);
        Assert.True(bubble.Y > 450);
    }
}
