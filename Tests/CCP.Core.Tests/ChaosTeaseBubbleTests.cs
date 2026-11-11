using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosTeaseBubbleTests
{
    [Fact]
    public void Tease_moves_until_denied_once_and_dissolves_without_a_miss()
    {
        var field = new AmbientBubbleField();
        var bubble = AmbientBubble.SpawnChaos(new Random(3), 0, 0, 0, 1920, 1080, 1,
            ChaosBubbleVariants.BuildTease(0.5));
        field.Bubbles.Add(bubble);
        int denied = 0;
        field.ChaosTeaseDenied = b => { Assert.Same(bubble, b); denied++; };
        Assert.InRange(bubble.Y, 50, 1080 - bubble.Size - 50);
        var x = bubble.X;
        var y = bubble.Y;
        field.HoldChaos = true;
        for (int i = 0; i < 200; i++) field.Step();
        Assert.Equal(0, denied);
        Assert.Equal(x, bubble.X);
        Assert.Equal(y, bubble.Y);
        field.HoldChaos = false;
        Assert.Equal(0, field.Step());
        Assert.Equal(1 + 0.10 * Math.Sin(0.02 * 5.2), bubble.DrawScale, 8);
        for (int i = 0; i < 186; i++) Assert.Equal(0, field.Step());
        Assert.Equal(0, denied);
        Assert.True(x != bubble.X || y != bubble.Y);
        Assert.Equal(0, field.Step());
        Assert.Equal(1, denied);
        Assert.True(bubble.Popping);
        for (int i = 0; i < 30; i++) Assert.Equal(0, field.Step());
        Assert.Empty(field.Bubbles);
        Assert.Equal(1, denied);
    }
}
