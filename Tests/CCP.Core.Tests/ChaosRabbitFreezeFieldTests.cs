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

public sealed class ChaosRabbitPaceTests
{
    // WPF BubbleService.cs:3326-3329,3890-3914: the size and 50 offsets cancel.
    [Fact]
    public void RabbitReflectsAtWorkingAreaAndEscapesAfterThreeBounces()
    {
        var spec = new EffectBubbleSpec { IsDarter = true, SizePx = 80, DarterSpeed = 9, LifetimeMs = 50000 };
        var rabbit = AmbientBubble.SpawnChaos(new Random(7), 0, 300, 200, 1600, 1000, 2, spec);
        const double top = 100, bottom = 520;
        rabbit.Y = top - 100; rabbit.Step(); Assert.Equal(top, rabbit.Y);
        rabbit.Y = bottom + 100; rabbit.Step(); Assert.Equal(bottom, rabbit.Y);
        rabbit.Y = top - 100; rabbit.Step(); Assert.Equal(top, rabbit.Y);
        rabbit.Y = bottom + rabbit.Size + 81;
        Assert.Equal(AmbientBubble.Result.Done, rabbit.Step());
    }

    [Fact]
    public void SlowMotionScalesChaosMovementAndFusesButNotAmbient()
    {
        var field = new AmbientBubbleField { ChaosTimeScale = 0.12 };
        var slow = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
        var normal = AmbientBubble.SpawnChaos(new Random(4), 0, 0, 0, 1920, 1080, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
        var y = slow.Y;
        field.Bubbles.Add(slow); field.Step(); normal.Step();
        Assert.Equal(32 * 0.12, 5000 - slow.FuseRemainingMs, 6);
        Assert.Equal((y - normal.Y) * 0.12, y - slow.Y, 6);
        Assert.Equal(32, 5000 - normal.FuseRemainingMs, 6);
        var spec = new EffectBubbleSpec { IsDarter = true, DarterSpeed = 9, LifetimeMs = 8000 };
        var slowRabbit = AmbientBubble.SpawnChaos(new Random(6), 0, 0, 0, 1920, 1080, 1, spec);
        var normalRabbit = AmbientBubble.SpawnChaos(new Random(6), 0, 0, 0, 1920, 1080, 1, spec);
        double rx = slowRabbit.X, ry = slowRabbit.Y;
        field.Bubbles.Add(slowRabbit); field.Step(); normalRabbit.Step();
        Assert.Equal((normalRabbit.X - rx) * 0.12, slowRabbit.X - rx, 6);
        Assert.Equal((normalRabbit.Y - ry) * 0.12, slowRabbit.Y - ry, 6);
        var settings = new ConditioningControlPanel.Models.AppSettings();
        var ambient = AmbientBubble.Spawn(new Random(6), 0, 0, 0, 1920, 1080, 1, settings, null, true);
        var control = AmbientBubble.Spawn(new Random(6), 0, 0, 0, 1920, 1080, 1, settings, null, true);
        field.Bubbles.Add(ambient); field.Step(); control.Step();
        Assert.Equal(control.Y, ambient.Y, 6);
    }
}
