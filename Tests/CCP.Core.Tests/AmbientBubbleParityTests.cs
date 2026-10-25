using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AmbientBubbleParityTests
{
    [Theory]
    [InlineData(BubbleMotionStyle.Rain, false, true, MotionLevel.Full, BubbleMotionStyle.FloatUp)]
    [InlineData(BubbleMotionStyle.Rain, true, false, MotionLevel.Off, BubbleMotionStyle.FloatUp)]
    [InlineData(BubbleMotionStyle.SpiralIn, false, true, MotionLevel.Reduced, BubbleMotionStyle.SpiralIn)]
    public void Ownership_and_motion_off_gate_styles(BubbleMotionStyle picked, bool rain, bool spiral, MotionLevel level, BubbleMotionStyle expected)
        => Assert.Equal(expected, AmbientBubbleMotionRules.Resolve(picked, rain, spiral, level, 0.8));

    [Fact]
    public void Mix_weights_only_owned_styles()
    {
        Assert.Equal(BubbleMotionStyle.FloatUp, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 0));
        Assert.Equal(BubbleMotionStyle.Rain, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 0.4));
        Assert.Equal(BubbleMotionStyle.SpiralIn, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, true, true, MotionLevel.Full, 0.9));
    }

    [Theory]
    [InlineData(BubbleMotionStyle.Rain)]
    [InlineData(BubbleMotionStyle.SpiralIn)]
    public void Motion_exits_quietly_without_a_pop(BubbleMotionStyle motion)
    {
        var s = new AppSettings { MotionLevel = MotionLevel.Full };
        var b = AmbientBubble.Spawn(new Random(42), 0, -800, -200, 1280, 720, 1.5, s, null, true, motion);
        var firstY = b.Y;
        var result = b.Step();
        if (motion == BubbleMotionStyle.Rain) Assert.True(b.Y > firstY);
        var steps = 1;
        while (result == AmbientBubble.Result.Alive && steps++ < 10000) result = b.Step();
        Assert.Equal(AmbientBubble.Result.Missed, result);
        Assert.False(b.Popping);
        Assert.InRange(b.Fade, 0, 1);
        Assert.Empty(b.Sparkles);
    }

    [Fact]
    public void Reduced_motion_halves_rain_speed()
    {
        AmbientBubble Spawn(MotionLevel level) => AmbientBubble.Spawn(new Random(4), 0, 0, 0, 1280, 720, 1,
            new AppSettings { MotionLevel = level }, null, true, BubbleMotionStyle.Rain);
        var full = Spawn(MotionLevel.Full); var reduced = Spawn(MotionLevel.Reduced);
        var fy = full.Y; var ry = reduced.Y;
        full.Step(); reduced.Step();
        Assert.Equal((full.Y - fy) / 2, reduced.Y - ry, 8);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 8)]
    [InlineData(true, false, 16)]
    public void Sparkles_follow_wpf_count_gravity_and_fade(bool lucky, bool boosted, int count)
    {
        var b = new AmbientBubble { Popping = true, Lucky = lucky };
        b.StartSparkles(new Random(7), boosted);
        Assert.Equal(count, b.Sparkles.Length);
        foreach (var sp in b.Sparkles)
        {
            var vy = sp.VelY;
            Assert.InRange(sp.Size, lucky ? 4 : 3, lucky ? 8 : 6);
            b.Step();
            Assert.True(sp.Alpha < 1);
            Assert.True(sp.VelY > vy);
            break;
        }
    }

    [Fact]
    public void Trigger_pool_respects_switch_chance_cap_and_retired_pull()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 50, BubbleTriggerVariants = new() { "flash" } };
        Assert.NotNull(AmbientBubbleTriggers.Roll(s, new Random(1), 3, false));
        Assert.Null(AmbientBubbleTriggers.Roll(s, new Random(1), 4, false));
        s.BubbleTriggerChance = 0;
        Assert.Null(AmbientBubbleTriggers.Roll(s, new Random(1), 0, false));
        s.BubbleTriggerChance = 50; s.BubbleTriggersEnabled = false;
        Assert.Null(AmbientBubbleTriggers.Roll(s, new Random(1), 0, false));
        Assert.Null(AmbientBubbleTriggers.Build(MagnetBubble.VariantId, "Mixed"));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Brain_drain_requires_both_ownership_and_toggle(bool owned, bool enabled, bool expected)
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 50,
            BubbleTriggerVariants = new(), BubbleBrainDrainEnabled = enabled };
        var spec = AmbientBubbleTriggers.Roll(s, new Random(1), 0, owned);
        Assert.Equal(expected, spec != null);
        if (expected) Assert.Equal(BrainDrainBubble.VariantId, spec!.VariantId);
    }

    [Fact]
    public void Triggers_are_benign_linger_and_expire_without_detonating()
    {
        var spec = AmbientBubbleTriggers.Build("flash", "RainDown")!;
        Assert.False(spec.IsLive);
        Assert.Equal(0, spec.FuseMs);
        Assert.Equal(ChaosMotion.RainDown, spec.Motion);
        Assert.Equal(2.5, spec.Payload.DurationMult);
        Assert.Equal(1.2, AmbientBubbleTriggers.Build("glitch", "Mixed")!.Payload.DurationMult);
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1280, 720, 1, spec);
        b.AmbientTrigger = true;
        AmbientBubble.Result result = AmbientBubble.Result.Alive;
        for (var i = 0; i < 220 && result == AmbientBubble.Result.Alive; i++) result = b.Step();
        Assert.Equal(AmbientBubble.Result.Missed, result);
        Assert.False(b.Popping);
    }

    [Theory]
    [InlineData("RainDown")]
    [InlineData("RoamBounce")]
    [InlineData("SideDrift")]
    public void Trigger_motion_moves_and_uses_spawn_speed(string motion)
    {
        var spec = AmbientBubbleTriggers.Build("pink", motion)!;
        var normal = AmbientBubble.SpawnChaos(new Random(3), 0, 0, 0, 1280, 720, 1, spec);
        var fast = AmbientBubble.SpawnChaos(new Random(3), 0, 0, 0, 1280, 720, 1, spec);
        normal.SetAmbientTrigger(0); fast.SetAmbientTrigger(100);
        var x = normal.X; var y = normal.Y;
        normal.Step(); fast.Step();
        Assert.True(normal.X != x || normal.Y != y);
        if (motion == "RainDown") Assert.Equal(2 * (normal.Y - y), fast.Y - y, 8);
        if (motion == "SideDrift") Assert.Equal(2 * (normal.X - x), fast.X - x, 8);
    }

    [Fact]
    public void Pop_credit_unlocks_at_1000_awards_points_and_flushes_every_50()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-bubble-credit-");
        var s = CoreSettings.Current; var points = s.SkillPoints;
        try
        {
            var path = Path.Combine(dir.FullName, "achievements.json");
            var engine = new AchievementEngine(new AchievementStore(path));
            engine.Progress.TotalBubblesPopped = 949;
            engine.TrackBubblePopped();
            Assert.Equal(950, new AchievementStore(path).Load().TotalBubblesPopped);
            Assert.Equal(points, s.SkillPoints);
            for (var i = 0; i < 50; i++) engine.TrackBubblePopped();
            Assert.True(engine.Progress.IsUnlocked("pop_the_thought"));
            Assert.Equal(points + 1, s.SkillPoints);
        }
        finally { s.SkillPoints = points; dir.Delete(true); }
    }
}
