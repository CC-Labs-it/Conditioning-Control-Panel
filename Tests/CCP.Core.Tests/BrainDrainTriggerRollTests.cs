using System;
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
    // WPF BubbleService.cs:1150/:1202 through the trunk roll (AmbientBubblePayloads, restack b231 #4520).
    public void Ownership_and_setting_gate_the_single_trigger_and_its_spec()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100,
            BubbleBrainDrainEnabled = true, BubbleTriggerVariants = new List<string> { BrainDrainBubble.VariantId } };
        var random = new Random(7);
        for (int i = 0; i < 100; i++) Assert.Null(AmbientBubblePayloads.Roll(s, random, 0, false));
        s.BubbleBrainDrainEnabled = false;
        for (int i = 0; i < 100; i++) Assert.Null(AmbientBubblePayloads.Roll(s, random, 0, true));
        s.BubbleBrainDrainEnabled = true;
        var pool = BrainDrainBubble.RollPool(new[] { "htlink" }, true, true);
        Assert.Equal(1, pool.Count(x => x == BrainDrainBubble.VariantId));
        EffectBubbleSpec? spec = null;
        for (int i = 0; i < 100 && spec == null; i++) spec = AmbientBubblePayloads.Roll(s, random, 0, true);
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
}
