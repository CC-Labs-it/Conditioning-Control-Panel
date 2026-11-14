using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class BrainDrainTriggerRollTests
{
    [Fact]
    public void Ownership_and_setting_gate_the_single_trigger_and_its_spec()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100,
            BubbleBrainDrainEnabled = true, BubbleTriggerVariants = new List<string> { BrainDrainBubble.VariantId } };
        var field = new AmbientBubbleField();
        for (int i = 0; i < 100; i++) Assert.Null(field.RollGifRain(s, false));
        s.BubbleBrainDrainEnabled = false;
        for (int i = 0; i < 100; i++) Assert.Null(field.RollGifRain(s, true));
        s.BubbleBrainDrainEnabled = true;
        var pool = BrainDrainBubble.RollPool(new[] { "htlink" }, true, true);
        Assert.Equal(1, pool.Count(x => x == BrainDrainBubble.VariantId));
        EffectBubbleSpec? spec = null;
        for (int i = 0; i < 100 && spec == null; i++) spec = field.RollGifRain(s, true);
        Assert.NotNull(spec);
        Assert.IsType<BrainDrainMeltPayload>(spec.Payload);
        Assert.True(spec.Payload!.Ambient);
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
        Assert.Equal(7000, spec.TreatLifeMs);
        Assert.Equal(220, spec.SizePx);
        var bubble = AmbientBubble.SpawnChaos(new System.Random(1), 0, 0, 0, 1920, 100000, 1, spec);
        Assert.Equal(BrainDrainBubble.PulseAt(0), bubble.DisplayFade, 8);
        bubble.Step();
        Assert.Equal(BrainDrainBubble.PulseAt(0.02), bubble.DisplayFade, 8);
    }
    [Theory]
    [InlineData("FloatUp", ChaosMotion.FloatUp)]
    [InlineData("RainDown", ChaosMotion.RainDown)]
    [InlineData("RoamBounce", ChaosMotion.RoamBounce)]
    [InlineData("SideDrift", ChaosMotion.SideDrift)]
    [InlineData("Mixed", ChaosMotion.FloatUp)]
    public void Trigger_uses_configured_motion(string mode, ChaosMotion expected)
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 50,
            BubbleBrainDrainEnabled = true, BubbleTriggerVariants = new List<string>(), ChaosMotionMode = mode };
        var field = new AmbientBubbleField();
        EffectBubbleSpec? spec = null;
        for (int i = 0; i < 100 && spec == null; i++) spec = field.RollGifRain(s, true);
        Assert.NotNull(spec);
        Assert.Equal(expected, spec.Motion);
        var b = AmbientBubble.SpawnChaos(new System.Random(3), 0, 0, 0, 1920, 1080, 1, spec);
        var x = b.X; var y = b.Y;
        b.Step();
        if (expected == ChaosMotion.RainDown) { Assert.Equal(-220, y); Assert.True(b.Y > y); }
        else if (expected == ChaosMotion.FloatUp) { Assert.Equal(1080, y); Assert.True(b.Y < y); }
        else if (expected == ChaosMotion.SideDrift) Assert.NotEqual(x, b.X);
        else { Assert.InRange(y, 0, 860); Assert.True(b.X != x || b.Y != y); }
    }
}
