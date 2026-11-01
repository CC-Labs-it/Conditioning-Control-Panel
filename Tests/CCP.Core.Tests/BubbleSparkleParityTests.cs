using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BubbleSparkleParityTests
{
    private sealed class FixedRandom(double value) : Random { public override double NextDouble() => value; }

    // WPF BubbleService.cs:1104-1108,3791-3813,4401-4470.
    [Fact]
    public void Paid_lucky_and_boost_pops_spawn_and_advance_their_particles()
    {
        var s = new AppSettings { UnlockedSkills = ["lucky_bubbles"], FlashGlowEnabled = false };
        var field = new AmbientBubbleField { Random = new FixedRandom(0) };
        var gold = new AmbientBubble();
        Assert.Equal(100, field.Pop(gold, s));
        Assert.True(gold.Lucky);
        Assert.Equal(16, gold.Sparkles.Length);
        Assert.All(gold.Sparkles, p => { Assert.Equal(4, p.Size); Assert.Equal(2, p.VelX); Assert.Equal(0, p.VelY); });
        gold.Step();
        Assert.Equal(2, gold.Sparkles[0].X);
        Assert.Equal(0.15, gold.Sparkles[0].VelY);
        Assert.Equal(0.96, gold.Sparkles[0].Alpha, 5);

        s.UnlockedSkills.Add("sparkle_boost_1");
        s.AmbientBubbleXpPaidToday = AmbientBubbleXp.DailyXpCap;
        var dry = new AmbientBubble();
        Assert.Equal(0, field.Pop(dry, s));
        Assert.False(dry.Lucky);
        Assert.Empty(dry.Sparkles); // glow setting is off
        s.FlashGlowEnabled = true;
        var pink = new AmbientBubble();
        Assert.Equal(0, field.Pop(pink, s));
        Assert.False(pink.Lucky);
        Assert.Equal(8, pink.Sparkles.Length);
        Assert.All(pink.Sparkles, p => Assert.Equal(3, p.Size));
        pink.Step();
        Assert.Equal(0.94, pink.Sparkles[0].Alpha, 5);
        s.UnlockedSkills.Clear();
        var plain = new AmbientBubble();
        field.Pop(plain, s);
        Assert.Empty(plain.Sparkles);
    }
}
