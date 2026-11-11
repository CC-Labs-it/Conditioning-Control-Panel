using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosBrittleTests
{
    [Fact]
    public void Natural_touch_waits_for_arm_and_reports_once()
    {
        var f = new AmbientBubbleField();
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1,
            ChaosBubbleVariants.BuildBrittle(1));
        f.Bubbles.Add(b);
        int shattered = 0;
        f.ChaosBrittleShattered = _ => shattered++;
        for (int i = 0; i < 29; i++) { f.CursorX = b.CenterX; f.CursorY = b.CenterY; f.Step(); }
        Assert.Equal(0, shattered);
        f.CursorX = b.CenterX + b.Size / 2 + 1; f.CursorY = b.CenterY; f.Step();
        Assert.Equal(0, shattered);
        f.CursorX = b.CenterX; f.CursorY = b.CenterY; f.Step();
        Assert.Equal(1, shattered);
        f.Step();
        Assert.Equal(1, shattered);
    }

    [Fact]
    public void Held_field_cannot_shatter_even_when_armed()
    {
        var f = new AmbientBubbleField();
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1,
            ChaosBubbleVariants.BuildBrittle(1));
        f.Bubbles.Add(b);
        for (int i = 0; i < 29; i++) f.Step();
        int shattered = 0;
        f.ChaosBrittleShattered = _ => shattered++;
        f.HoldChaos = true;
        f.CursorX = b.CenterX; f.CursorY = b.CenterY;
        for (int i = 0; i < 50; i++) f.Step();
        Assert.Equal(0, shattered);
        Assert.False(b.Popping);
        f.HoldChaos = false;
        f.Step();
        Assert.Equal(1, shattered);
    }

    [Fact]
    public void Sweep_does_not_snap_the_mine()
    {
        var f = new AmbientBubbleField();
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1,
            ChaosBubbleVariants.BuildBrittle(1));
        f.Bubbles.Add(b);
        f.DefuseAllLive();
        Assert.False(b.Popping);
        Assert.Single(f.Bubbles);
    }
}
