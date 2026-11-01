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
    [Fact]
    public void Freeze_shudders_only_chaos_bubbles_for_200ms_and_clears_on_thaw()
    {
        var f = new AmbientBubbleField();
        var chaos = new AmbientBubble { Spec = new EffectBubbleSpec(), X = 40, Y = 50, Size = 100, Clickable = true };
        var ambient = new AmbientBubble { X = 40, Y = 50, Size = 100 };
        f.Bubbles.AddRange(new[] { chaos, ambient });
        f.StartFreeze();
        f.Step();
        Assert.Equal(168, f.FreezeImpactMs);
        Assert.Equal(chaos.X + chaos.Size / 2, chaos.CenterX);
        double offset = chaos.FreezeOffsetX;
        chaos.FreezeOffsetX = 4;
        Assert.True(chaos.Contains(chaos.X + 0.5, chaos.CenterY));
        chaos.FreezeOffsetX = offset;
        Assert.InRange(chaos.FreezeOffsetX, -4, 4);
        Assert.True(chaos.FreezeOffsetX != 0 || chaos.FreezeOffsetY != 0);
        Assert.Equal((0, 0), (ambient.FreezeOffsetX, ambient.FreezeOffsetY));
        double edge = chaos.FreezeOffsetX > 0 ? chaos.X + chaos.Size + chaos.FreezeOffsetX / 2
            : chaos.X + chaos.FreezeOffsetX / 2;
        Assert.Same(chaos, f.HitTest(0, edge, chaos.CenterY + chaos.FreezeOffsetY));
        Assert.Equal(0.18, chaos.FreezePulsePhase, 5);
        for (int i = 0; i < 6; i++) f.Step();
        Assert.Equal(0, f.FreezeImpactMs);
        Assert.Equal((0, 0), (chaos.FreezeOffsetX, chaos.FreezeOffsetY));
        Assert.Equal(1.26, chaos.FreezePulsePhase, 5);
        f.StartFreeze(); f.Step();
        f.EndFreeze();
        Assert.False(f.FreezeChaos);
        Assert.Equal(0, f.FreezeImpactMs);
        Assert.Equal((0, 0), (chaos.FreezeOffsetX, chaos.FreezeOffsetY));
    }
    [Fact]
    public void Aura_phase_pulses_only_while_frozen()
    {
        var f = new AmbientBubbleField();
        var first = new AmbientBubble { Spec = new EffectBubbleSpec() };
        f.Bubbles.Add(first);
        f.StartFreeze();
        f.Step();
        Assert.Equal(0.18, first.FreezePulsePhase, 5);
        f.Step();
        var second = new AmbientBubble { Spec = new EffectBubbleSpec() };
        f.Bubbles.Add(second);
        f.Step();
        Assert.Equal(0.54, first.FreezePulsePhase, 5);
        Assert.Equal(0.18, second.FreezePulsePhase, 5);
        f.EndFreeze();
        f.Step();
        Assert.Equal(0.54, first.FreezePulsePhase, 5);
    }

}
