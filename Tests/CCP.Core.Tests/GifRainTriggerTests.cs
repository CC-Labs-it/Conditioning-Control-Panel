using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class GifRainTriggerTests
{
    [Fact]
    public void Persisted_gif_rain_selection_rolls_a_benign_trigger_and_respects_gates()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100,
            BubbleTriggerVariants = new List<string> { "htlink" } };
        var field = new AmbientBubbleField();
        EffectBubbleSpec? spec = null;
        for (int i = 0; i < 100 && spec == null; i++) spec = field.RollGifRain(s);
        Assert.NotNull(spec);
        Assert.Equal("htlink", spec.VariantId);
        Assert.IsType<GifCascadePayload>(spec.Payload);
        Assert.True(spec.Payload!.Ambient);
        Assert.False(spec.IsLive);
        Assert.Equal(2.5, spec.Payload.DurationMult);
        s.BubbleTriggersEnabled = false;
        Assert.Null(field.RollGifRain(s));
        s.BubbleTriggersEnabled = true;
        s.BubbleTriggerChance = 0;
        Assert.Null(field.RollGifRain(s));
        s.BubbleTriggerChance = 100;
        s.BubbleTriggerVariants = new List<string> { "video" };
        Assert.Null(field.RollGifRain(s));
        s.BubbleTriggerVariants = new List<string> { "htlink" };
        for (int i = 0; i < 4; i++) field.Bubbles.Add(new AmbientBubble { Spec = spec });
        for (int i = 0; i < 100; i++) Assert.Null(field.RollGifRain(s));
    }

    [Fact]
    public void Chaos_pause_does_not_freeze_ambient_trigger()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100,
            BubbleTriggerVariants = new List<string> { "htlink" } };
        var field = new AmbientBubbleField { HoldChaos = true };
        EffectBubbleSpec? spec = null;
        for (int i = 0; i < 100 && spec == null; i++) spec = field.RollGifRain(s);
        Assert.NotNull(spec);
        var bubble = AmbientBubble.SpawnChaos(new System.Random(1), 0, 0, 0, 1920, 1080, 1, spec);
        field.Bubbles.Add(bubble);
        var y = bubble.Y;
        field.Step();
        Assert.True(bubble.Y < y);
    }
    [Fact]
    public void Unpopped_gif_rain_expires_after_seven_seconds_and_dissolves_without_a_payload()
    {
        var variant = System.Linq.Enumerable.First(ChaosBubbleVariants.All, v => v.Id == "htlink");
        var spec = ChaosBubbleVariants.Build(variant, intensity: 0.3, ambient: true);
        var field = new AmbientBubbleField();
        var bubble = AmbientBubble.SpawnChaos(new System.Random(1), 0, 0, 0, 1920, 100000, 1, spec);
        field.Bubbles.Add(bubble);
        for (int i = 0; i < 218; i++) field.Step();
        Assert.False(bubble.Popping);
        field.Step();
        Assert.True(bubble.Dissolving);
        Assert.Null(field.HitTest(0, bubble.CenterX, bubble.CenterY));
        var x = bubble.X; var y = bubble.Y; var angle = bubble.Angle;
        field.Step();
        Assert.Equal((x, y, angle), (bubble.X, bubble.Y, bubble.Angle));
        Assert.Equal(0.98, bubble.Scale, 8);
        Assert.Equal(0.95, bubble.Fade, 8);
        for (int i = 0; i < 30; i++) field.Step();
        Assert.Empty(field.Bubbles);
    }

    /// <summary>WPF BubbleService.cs:4229: the treat breathes faster over its last 1500 ms.</summary>
    [Fact]
    public void Gif_rain_telegraphs_its_last_one_and_a_half_seconds()
    {
        var variant = System.Linq.Enumerable.First(ChaosBubbleVariants.All, v => v.Id == "htlink");
        var bubble = AmbientBubble.SpawnChaos(new System.Random(1), 0, 0, 0, 1920, 100000, 1,
            ChaosBubbleVariants.Build(variant, intensity: 0.3, ambient: true));
        for (int i = 0; i < 171; i++) bubble.Step(false, 0, 0, System.DateTime.UtcNow);
        Assert.Equal(0, bubble.DangerFactor);   // 1528 ms left
        bubble.Step(false, 0, 0, System.DateTime.UtcNow);
        Assert.InRange(bubble.DangerFactor, 0.001, 0.01);   // 1496 ms left
        for (int i = 0; i < 46; i++) bubble.Step(false, 0, 0, System.DateTime.UtcNow);
        Assert.InRange(bubble.DangerFactor, 0.98, 0.99);   // 24 ms left
    }

    /// <summary>WPF BubbleService.cs:1242 counts live benign treats only: live chaos bubbles and
    /// popping ones do not hold the four-trigger ceiling.</summary>
    [Fact]
    public void Trigger_ceiling_ignores_live_chaos_and_popping_bubbles()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 100,
            BubbleTriggerVariants = new List<string> { "htlink" } };
        var field = new AmbientBubbleField();
        for (int i = 0; i < 4; i++) field.Bubbles.Add(new AmbientBubble { Spec = new EffectBubbleSpec { IsLive = true } });
        for (int i = 0; i < 4; i++) field.Bubbles.Add(new AmbientBubble { Spec = new EffectBubbleSpec(), Popping = true });
        EffectBubbleSpec? spec = null;
        for (int i = 0; i < 100 && spec == null; i++) spec = field.RollGifRain(s);
        Assert.NotNull(spec);
    }
}
