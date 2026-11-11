using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

// WPF BubbleService.cs:3325-3348,3380-3400,3834-3915,3951-3952.
public sealed class ChaosDarterMotionTests
{
    [Fact]
    public void Pinned_darter_flares_then_moves_bounces_and_exits_without_a_miss()
    {
        var spec = ChaosBubbleVariants.BuildDarter(0, atPxX: 400, atPxY: 300);
        var field = new AmbientBubbleField();
        var b = AmbientBubble.SpawnChaos(new Random(4), 2, 200, 100, 400, 400, 2, spec);
        field.Bubbles.Add(b);
        Assert.Equal(200, b.CenterX, 6);
        Assert.Equal(150, b.CenterY, 6);
        var x = b.X; var y = b.Y;
        for (int i = 0; i < 13; i++) { Assert.Equal(0, field.Step()); Assert.Equal(x, b.X); Assert.Equal(y, b.Y); }
        field.Step();
        Assert.Equal(spec.DarterSpeed, Math.Sqrt(Math.Pow(b.X - x, 2) + Math.Pow(b.Y - y, 2)), 6);
        bool sawBounce = false;
        double previousX = b.X - x, previousY = b.Y - y;
        for (int i = 0; i < 220 && field.Bubbles.Count > 0; i++)
        {
            x = b.X; y = b.Y;
            Assert.Equal(0, field.Step());
            double dx = b.X - x, dy = b.Y - y;
            if (dx * previousX < 0 || dy * previousY < 0) sawBounce = true;
            previousX = dx; previousY = dy;
        }
        Assert.True(sawBounce);
        Assert.Empty(field.Bubbles); // three reflections followed by exit, not a FloatUp miss
    }

    [Fact]
    public void Flare_fraction_and_size_follow_the_darter_clock()
    {
        var spec = new EffectBubbleSpec { IsDarter = true, SizePx = 80.6, TelegraphMs = 64, LifetimeMs = 8000 };
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1, spec);
        Assert.Equal(81, b.Size);
        Assert.Equal(1, b.DarterFlareFraction);
        b.Step();
        Assert.Equal(0.5, b.DarterFlareFraction);
        b.Step();
        Assert.Equal(0, b.DarterFlareFraction);
    }

    private sealed class HorizontalRandom : Random
    {
        public override double NextDouble() => 0;
    }

    [Fact]
    public void Darter_reflects_three_times_before_leaving()
    {
        var spec = new EffectBubbleSpec { IsDarter = true, SizePx = 80, DarterSpeed = 9,
            LifetimeMs = 8000, SpawnAtPxX = 130, SpawnAtPxY = 130 };
        var field = new AmbientBubbleField();
        var b = AmbientBubble.SpawnChaos(new HorizontalRandom(), 0, 0, 0, 260, 260, 1, spec);
        field.Bubbles.Add(b);
        int direction = 1, turns = 0;
        for (int i = 0; i < 250 && field.Bubbles.Count > 0; i++)
        {
            double x = b.X;
            Assert.Equal(0, field.Step());
            int next = Math.Sign(b.X - x);
            if (next != 0 && next != direction)
            {
                if (turns == 0) Assert.Equal(1 + 0.10 * Math.Sin((i + 1) * 0.5) + 0.17, b.Scale, 6);
                turns++; direction = next;
            }
        }
        Assert.Equal(3, turns);
        Assert.Empty(field.Bubbles);
    }

    [Fact]
    public void Darter_safety_lifetime_and_pop_burst_are_distinct_from_a_treat()
    {
        var spec = new EffectBubbleSpec { IsDarter = true, SizePx = 80, TelegraphMs = 400, LifetimeMs = 64 };
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1, spec);
        Assert.Equal(AmbientBubble.Result.Alive, b.Step());
        Assert.Equal(1 + 0.10 * Math.Sin(0.5), b.Scale, 6);
        Assert.Equal(AmbientBubble.Result.Done, b.Step());
        b.Popping = true;
        b.Scale = 1; b.Fade = 1;
        b.Step();
        Assert.Equal(1.07, b.Scale, 6);
        Assert.Equal(0.97, b.Fade, 6);
        Assert.Equal(b.Scale, b.DrawScale);
    }
}
