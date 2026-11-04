using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosFreezeFieldTests
{
    [Fact]
    public void Freeze_holds_motion_and_fuse_but_allows_defuse_channel()
    {
        var field = new AmbientBubbleField { FreezeChaos = true };
        var bubble = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
        field.Bubbles.Add(bubble);
        double y = bubble.Y, fuse = bubble.FuseRemainingMs;
        field.Step();
        Assert.Equal(y, bubble.Y);
        Assert.Equal(fuse, bubble.FuseRemainingMs);
        field.PressLive(bubble, true, bubble.CenterX, bubble.CenterY);
        Assert.True(bubble.Channeling);
        bool snapped = false;
        field.ChaosDefused = _ => snapped = true;
        var now = DateTime.UtcNow;
        field.Clock = () => now.AddSeconds(10);
        field.Step();
        Assert.True(snapped);
        field.FreezeChaos = false;
        var next = AmbientBubble.SpawnChaos(new Random(2), 0, 0, 0, 1920, 1080, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
        field.Bubbles.Add(next);
        field.Step();
        Assert.True(next.FuseRemainingMs < 5000);
    }
}
