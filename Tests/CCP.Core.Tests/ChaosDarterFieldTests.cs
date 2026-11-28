using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosDarterFieldTests
{
    private static AmbientBubble Rabbit(double width = 400, double height = 400) =>
        AmbientBubble.SpawnChaos(new Random(7), 0, 0, 0, width, height, 1,
            ChaosBubbleVariants.BuildDarter(0.5), ChaosMotion.RoamBounce);

    [Fact]
    public void Telegraph_holds_then_darter_moves_at_spec_speed()
    {
        var b = Rabbit();
        double x = b.X, y = b.Y;
        for (int i = 0; i < 12; i++) Assert.Equal(AmbientBubble.Result.Alive, b.Step());
        Assert.Equal(x, b.X);
        Assert.Equal(y, b.Y);
        b.Step();
        b.Step();
        Assert.Equal(ChaosBubbleVariants.DARTER_SPEED, Math.Sqrt(Math.Pow(b.X - x, 2) + Math.Pow(b.Y - y, 2)), 5);
    }

    [Fact]
    public void Darter_bounces_then_escapes_instead_of_roaming_forever()
    {
        var b = Rabbit();
        bool leftBounds = false;
        AmbientBubble.Result result = AmbientBubble.Result.Alive;
        for (int i = 0; i < 300 && result == AmbientBubble.Result.Alive; i++)
        {
            result = b.Step();
            leftBounds |= b.X < 0 || b.X > 400 - b.Size || b.Y < 0 || b.Y > 400 - b.Size;
        }
        Assert.True(leftBounds);
        Assert.Equal(AmbientBubble.Result.Done, result);
    }

    [Fact]
    public void Darter_lifetime_runs_at_field_pace()
    {
        var b = Rabbit(100000, 100000);
        for (int i = 0; i < 250; i++) Assert.Equal(AmbientBubble.Result.Alive, b.Step(timeScale: 0.12));
        AmbientBubble.Result result = AmbientBubble.Result.Alive;
        for (int i = 0; i < 2400 && result == AmbientBubble.Result.Alive; i++) result = b.Step(timeScale: 0.12);
        Assert.Equal(AmbientBubble.Result.Done, result);
    }
}
