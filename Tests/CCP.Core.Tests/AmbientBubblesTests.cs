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
    [Theory]
    [InlineData(false, false, false, false, 0)]
    [InlineData(true, false, true, false, 8)]
    [InlineData(true, false, false, false, 0)]
    [InlineData(false, true, false, false, 16)]
    [InlineData(false, true, true, true, 0)]
    [InlineData(true, true, true, true, 8)]
    public void Pop_particles_follow_skill_glow_and_paid_lucky_rules(bool boost, bool lucky, bool glow, bool dry, int count)
    {
        // WPF BubbleService.cs:1105-1108, :4430-4472.
        var s = new AppSettings { FlashGlowEnabled = glow };
        if (boost) s.UnlockedSkills.Add("sparkle_boost_1");
        if (lucky) s.UnlockedSkills.Add("lucky_bubbles");
        if (dry) AmbientBubbleXp.Take(s, AmbientBubbleXp.DailyXpCap);
        var field = new AmbientBubbleField(new LuckyRandom());
        var b = new AmbientBubble { Size = 100 };
        field.Pop(b, s);
        Assert.Equal(count, b.Sparkles.Length);
        Assert.Equal(lucky && !dry, b.Lucky);
        var particles = b.Sparkles;
        field.Pop(b, s);
        Assert.Same(particles, b.Sparkles); // repeat input must not restart the burst
        foreach (var sp in particles)
        {
            Assert.Equal(50, sp.X);
            Assert.Equal(50, sp.Y);
            Assert.Equal(1, sp.Alpha);
            Assert.Equal(2.04, Math.Sqrt(sp.VelX * sp.VelX + sp.VelY * sp.VelY), 8);
            Assert.Equal(b.Lucky ? 4.04 : 3.03, sp.Size, 8);
        }
        b.Step();
        for (var i = 0; i < particles.Length; i++)
        {
            var sp = particles[i];
            Assert.Equal(50 + sp.VelX, sp.X, 8);
            Assert.Equal(50 + sp.VelY - 0.15, sp.Y, 8);
            Assert.Equal(b.Lucky ? 0.96 : 0.94, sp.Alpha, 8);
        }
        field.Bubbles.Add(b);
        for (var i = 0; i < 24; i++) field.Step();
        Assert.Empty(field.Bubbles); // particles cannot keep a completed bubble alive
    }

    private sealed class LuckyRandom : Random
    {
        public override double NextDouble() => 0.01; // force a lucky roll without a probabilistic test
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
        var b = AmbientBubble.Spawn(new Random(1), 0, 0, 0, 1920, 1080, 1, s, null, true);
        Assert.Equal(1080, b.Y);                                // WPF: starts at the bottom
        Assert.InRange(b.Size, BubbleSizing.BaseMinDip, BubbleSizing.BaseMaxDip);
        f.Bubbles.Add(b);
        var y = b.Y;
        f.Step();
        Assert.InRange(y - b.Y, 1.0, 2.0);                      // 1..2 DIP a step
        var missed = 0;
        for (var i = 0; i < 2000 && f.Bubbles.Count > 0; i++) missed += f.Step();
        Assert.Equal(1, missed);                                // left through the top

        var p = AmbientBubble.Spawn(new Random(2), 0, 0, 0, 1920, 1080, 1, s, null, true);
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
        while (f.CanSpawn(s)) f.Bubbles.Add(AmbientBubble.Spawn(r, 0, 0, 0, 1920, 50000, 1, s, null, true));
        Assert.Equal(40, f.Bubbles.Count);
        f.Step();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) { f.Step(); f.HitTest(0, 500, 500); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
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
