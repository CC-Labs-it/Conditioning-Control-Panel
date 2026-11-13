using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF BubbleService ambient field: the daily XP bucket, FloatUp motion + pop burst, the
// allocation-free step, and the engine arming the field.
[Collection(SessionStatics.Name)]
public sealed class AmbientBubblesTests
{
    // WPF BubbleService.cs:3277-3286, ContainsPx :3023-3027 (restack b120, includes #3500).
    [Theory]
    [InlineData(false, 1.25, false, 250)]
    [InlineData(true, 1.25, true, 350)]
    [InlineData(false, 1.25, true, 250)]
    [InlineData(true, 4, true, 400)]
    [InlineData(true, 0.5, false, 200)]
    public void Chaos_hit_disc_samples_boon_and_live_magnet_without_growing_the_sprite(bool live, double scale, bool magnet, double expected)
    {
        var spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec { SizePx = 200, IsLive = live };
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1, spec);
        b.ApplyChaosHitbox(scale, magnet);
        Assert.Equal(200, b.Size);
        Assert.Equal(expected, b.HitSize);
        Assert.True(b.Contains(b.CenterX + expected / 2, b.CenterY));
        Assert.False(b.Contains(b.CenterX + expected / 2 + 0.01, b.CenterY));
    }

    [Theory]
    [InlineData("darter")]
    [InlineData("freeze")]
    [InlineData("golden")]
    [InlineData("heart")]
    [InlineData("droplet")]
    [InlineData("prism")]
    [InlineData("escort")]
    [InlineData("tease")]
    [InlineData("brittle")]
    public void Precision_pickups_keep_the_natural_hit_disc(string kind)
    {
        var spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec {
            SizePx = 200, IsLive = true, IsDarter = kind == "darter", IsFreeze = kind == "freeze",
            IsGolden = kind == "golden", IsHeart = kind == "heart", IsDroplet = kind == "droplet",
            IsPrism = kind == "prism", IsEscort = kind == "escort", IsTease = kind == "tease", IsBrittle = kind == "brittle" };
        Assert.Equal(200, AmbientBubble.ChaosHitSize(spec, 200, 2, true));
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1, spec);
        b.ApplyChaosHitbox(2, true);
        Assert.Equal(200, b.HitSize);
        Assert.False(b.Contains(b.CenterX + 101, b.CenterY));
    }

    [Theory]
    [InlineData(500, 300, 1.0)]
    [InlineData(-1500, 400, 1.5)]
    [InlineData(2200, -200, 2.0)]
    public void Chaos_pinned_spawn_centres_on_physical_point(double x, double y, double scale)
    {
        var spec = ConditioningControlPanel.Services.Chaos.ChaosBubbleVariants.BuildDarter(1, false, x, y);
        var b = AmbientBubble.SpawnChaos(new Random(7), 0, 0, 0, 1920, 1080, scale, spec);
        Assert.Equal(x / scale, b.CenterX, 8);
        Assert.Equal(y / scale, b.CenterY, 8);
        b.Step();
        Assert.InRange(Math.Abs(b.X - (x / scale - b.Size / 2)), 0, 45);
    }

    [Fact]
    public void Unpinned_chaos_spawn_still_starts_at_bottom()
    {
        var spec = ConditioningControlPanel.Services.Chaos.ChaosBubbleVariants.BuildDarter(1);
        var b = AmbientBubble.SpawnChaos(new Random(7), 0, 100, 200, 1920, 1080, 2, spec);
        Assert.Equal(640, b.Y);
    }

    [Fact]
    public void Bucket_pays_up_to_300_a_day_then_the_remainder_then_nothing()
    {
        var s = new AppSettings { AmbientBubbleXpDayKey = "2000-01-01", AmbientBubbleXpPaidToday = 999 };
        Assert.Equal(0, AmbientBubbleXp.PaidToday(s));          // stale day reads 0, untouched
        Assert.Equal(999, s.AmbientBubbleXpPaidToday);
        Assert.Equal(5, AmbientBubbleXp.Take(s, 5));            // rollover on first pop
        Assert.Equal(5, AmbientBubbleXp.PaidToday(s));
        s.AmbientBubbleXpPaidToday = 290;
        Assert.Equal(10, AmbientBubbleXp.Take(s, 100));         // lucky near the ceiling pays the remainder
        Assert.Equal(0, AmbientBubbleXp.Take(s, 5));
        Assert.Equal(0, AmbientBubbleXp.RemainingToday(s));
    }

    [Fact]
    public void Bubble_floats_up_off_screen_and_a_pop_bursts_out_in_16_steps()
    {
        var s = new AppSettings();
        var f = new AmbientBubbleField();
        var b = AmbientBubble.Spawn(new Random(1), 0, 0, 0, 1920, 1080, 1, s, null, true, MotionLevel.Full);
        Assert.Equal(1080, b.Y);                                // WPF: starts at the bottom
        Assert.InRange(b.Size, BubbleSizing.BaseMinDip, BubbleSizing.BaseMaxDip);
        f.Bubbles.Add(b);
        var y = b.Y;
        f.Step();
        Assert.InRange(y - b.Y, 1.0, 2.0);                      // 1..2 DIP a step
        var missed = 0;
        for (var i = 0; i < 2000 && f.Bubbles.Count > 0; i++) missed += f.Step();
        Assert.Equal(1, missed);                                // left through the top

        var p = AmbientBubble.Spawn(new Random(2), 0, 0, 0, 1920, 1080, 1, s, null, true, MotionLevel.Full);
        f.Bubbles.Add(p);
        Assert.Equal(5, f.Pop(p, s));
        Assert.Equal(0, f.Pop(p, s));                           // never twice
        var steps = 0;
        while (f.Bubbles.Count > 0) { Assert.Equal(0, f.Step()); steps++; }
        Assert.Equal(16, steps);                                // fade 0.066 a step
        Assert.True(p.Scale > 1.6);
    }

    [Fact]
    public void Step_and_hit_test_allocate_nothing_with_a_full_field()
    {
        var s = new AppSettings();
        var f = new AmbientBubbleField();
        var r = new Random(3);
        while (f.CanSpawn(s)) f.Bubbles.Add(AmbientBubble.Spawn(r, 0, 0, 0, 1920, 50000, 1, s, null, true, MotionLevel.Full));
        Assert.Equal(40, f.Bubbles.Count);
        f.Step();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { f.Step(); f.HitTest(0, 500, 500); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Freeze_visuals_pulse_and_shudder_only_chaos_bubbles_then_clear()
    {
        var field = new AmbientBubbleField(new Random(7)) { FreezeChaos = true, FreezeVibrateRemainingMs = 200 };
        var chaos = new AmbientBubble { X = 100, Y = 200, Size = 100, Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec() };
        var ambient = new AmbientBubble { X = 300, Y = 200, Size = 100 };
        field.Bubbles.Add(chaos);
        field.Bubbles.Add(ambient);
        double initialAura = field.FreezeAuraOpacity;
        field.Step();
        Assert.NotEqual(initialAura, field.FreezeAuraOpacity);
        Assert.InRange(field.FreezeAuraOpacity, 0.025, 0.2);
        Assert.NotEqual(0, chaos.FreezeOffsetX);
        Assert.InRange(chaos.FreezeOffsetX, -4, 4);
        Assert.InRange(chaos.FreezeOffsetY, -4, 4);
        Assert.Equal(0, ambient.FreezeOffsetX);
        Assert.True(chaos.Contains(chaos.CenterX, chaos.CenterY));
        for (int i = 0; i < 7; i++) field.Step();
        Assert.Equal(0, field.FreezeVibrateRemainingMs);
        Assert.Equal(0, chaos.FreezeOffsetX);
        Assert.Equal(0, chaos.FreezeOffsetY);
        Assert.True(field.FreezeAuraOpacity > 0);
        field.FreezeVibrateRemainingMs = 200;
        field.Step();
        field.FreezeChaos = false;
        field.ClearFreezeVisuals();
        Assert.Equal(0, field.FreezeAuraOpacity);
        Assert.Equal(0, field.FreezeVibrateRemainingMs);
        Assert.Equal(0, chaos.FreezeOffsetX);
        Assert.Equal(0, chaos.FreezeOffsetY);
    }

    [Fact]
    public void Engine_arms_bubbles_by_the_saved_flag_and_applies_them_live()
    {
        var s = CoreSettings.Current;
        int start = 0, stop = 0;
        CoreBubbles.StartAction = () => start++;
        CoreBubbles.StopAction = () => stop++;
        try
        {
            s.BubblesEnabled = true;
            CoreEngine.Start();
            Assert.Equal(1, start);
            CoreEngine.ApplyLive("bubbles", false);
            Assert.Equal(1, stop);
            CoreEngine.Stop();
            Assert.Equal(2, stop);
            CoreEngine.ApplyLive("bubbles", true);                // stopped: nothing
            Assert.Equal(1, start);
        }
        finally { CoreEngine.Stop(); CoreBubbles.StartAction = CoreBubbles.StopAction = null; s.BubblesEnabled = false; }
    }
}
