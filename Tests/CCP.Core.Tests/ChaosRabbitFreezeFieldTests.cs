using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosRabbitFreezeFieldTests
{
    [Fact]
    public void DarterTelegraphsMovesAndExpires()
    {
        var spec = ChaosBubbleVariants.BuildDarter(0.5);
        var rabbit = AmbientBubble.SpawnChaos(new Random(7), 0, 0, 0, 1920, 1080, 1, spec);
        double x = rabbit.X, y = rabbit.Y;
        for (int i = 0; i < 12; i++) Assert.Equal(AmbientBubble.Result.Alive, rabbit.Step());
        Assert.Equal((x, y), (rabbit.X, rabbit.Y));
        for (int i = 0; i < 2; i++) rabbit.Step();
        Assert.NotEqual((x, y), (rabbit.X, rabbit.Y));
        var result = AmbientBubble.Result.Alive;
        for (int i = 0; i < 300 && result == AmbientBubble.Result.Alive; i++) result = rabbit.Step();
        Assert.Equal(AmbientBubble.Result.Done, result);
    }

    [Fact]
    public void FreezeStopsMotionAndFuseButAllowsChannel()
    {
        var field = new AmbientBubbleField();
        var bubble = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
        field.Bubbles.Add(bubble); field.FreezeChaos = true;
        var y = bubble.Y; field.Step();
        Assert.Equal(y, bubble.Y); Assert.Equal(5000, bubble.FuseRemainingMs);
        field.PressLive(bubble, true, bubble.CenterX, bubble.CenterY);
        Assert.True(bubble.Channeling);
        field.FreezeChaos = false; bubble.CancelChannel(); field.Step();
        Assert.True(bubble.Y < y); Assert.True(bubble.FuseRemainingMs < 5000);
    }
}
