using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosFreezeFieldTests
{
    [Fact]
    public void Freeze_holds_motion_and_fuse_but_channels_and_pop_bursts_continue()
    {
        var now = DateTime.UtcNow;
        var field = new AmbientBubbleField { FreezeChaos = true, Clock = () => now };
        var live = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1,
            new EffectBubbleSpec { IsLive = true, FuseMs = 5000, SizePx = 100 });
        var other = AmbientBubble.SpawnChaos(new Random(2), 0, 0, 0, 1920, 1080, 1, new EffectBubbleSpec());
        field.Bubbles.AddRange(new[] { live, other });
        var (x, y, fuse) = (live.X, live.Y, live.FuseRemainingMs);
        field.Step();
        Assert.Equal((x, y, fuse), (live.X, live.Y, live.FuseRemainingMs));
        bool snapped = false;
        field.ChaosDefused = _ => snapped = true;
        field.PressLive(live, true, live.CenterX, live.CenterY);
        now = now.AddMilliseconds(ChaosTuning.DEFUSE_HOLD_MS + 1);
        field.Step();
        Assert.True(snapped);
        var fade = live.Fade;
        field.Step();
        Assert.True(live.Fade < fade);
        field.FreezeChaos = false;
        field.Step();
        Assert.NotEqual(1080, other.Y);
    }
}
