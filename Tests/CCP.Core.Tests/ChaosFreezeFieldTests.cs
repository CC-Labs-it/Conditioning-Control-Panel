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

    [Fact]
    public void Freeze_pulses_chaos_aura_and_shudders_only_for_200ms()
    {
        var field = new AmbientBubbleField { FreezeChaos = true, FreezeShudderMs = 200 };
        var chaos = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
        var plain = new AmbientBubble { Size = 100 };
        field.Bubbles.Add(chaos);
        field.Bubbles.Add(plain);
        int shudders = 0;
        for (int i = 0; i < 10; i++)
        {
            field.Step();
            Assert.InRange(chaos.FreezeAura, 0.025, 0.2);
            Assert.Equal((0d, 0d, 0d), (plain.FreezeAura, plain.JitterX, plain.JitterY));
            if (chaos.JitterX != 0 || chaos.JitterY != 0) shudders++;
            Assert.InRange(Math.Abs(chaos.JitterX), 0, 4);
        }
        Assert.Equal(6, shudders); // WPF: 200 ms less 32 ms a frame, checked after the decrement
        field.FreezeChaos = false;
        field.Step();
        Assert.Equal(0, chaos.FreezeAura);
    }
}
