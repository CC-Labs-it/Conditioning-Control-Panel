using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>ChaosVibeTrail against the WPF constants and animations (ChaosVibeTrailOverlay.cs).</summary>
public class ChaosVibeTrailTests
{
    [Fact]
    public void FirstTickDropsADotThenOnlyAfterNineDips()
    {
        var t = new ChaosVibeTrail();
        Assert.Equal(0, t.Move(100, 100, 0));
        Assert.Equal(-1, t.Move(108.9, 100, 16));   // under EMIT_DIST: glow follows, no dot
        Assert.Equal(108.9, t.GlowX);
        Assert.Equal(1, t.Move(109, 100, 32));      // exactly 9 from the last emit
        t.Begin();                                  // a new vibe: the next tick drops one where it stands
        Assert.Equal(2, t.Move(109, 100, 48));
    }

    [Fact]
    public void RingRecyclesFourteenSlots()
    {
        var t = new ChaosVibeTrail();
        for (int i = 0; i < ChaosVibeTrail.Dots; i++) Assert.Equal(i, t.Move(i * 10, 0, i));
        Assert.Equal(0, t.Move(1000, 0, 100));
        Assert.Equal(1000, t.Dot(0, 100).X);
    }

    [Fact]
    public void DotFadesHalfToZeroAndShrinksOver340Ms()
    {
        var t = new ChaosVibeTrail();
        t.Move(5, 6, 1000);
        Assert.Equal((5.0, 6.0, 0.5, 1.0), t.Dot(0, 1000));
        var mid = t.Dot(0, 1170);
        Assert.Equal(0.25, mid.Opacity, 6);
        Assert.Equal(0.675, mid.Scale, 6);
        var end = t.Dot(0, 1340 + 500);
        Assert.Equal(0, end.Opacity, 6);
        Assert.Equal(0.35, end.Scale, 6);
        Assert.Equal(0, t.Dot(1, 1000).Opacity);   // never born
        t.Clear();
        Assert.Equal(0, t.Dot(0, 1000).Opacity);
    }

    [Fact]
    public void GlowPulsesSineInOutBetween090And112Every360Ms()
    {
        Assert.Equal(0.9, ChaosVibeTrail.GlowScale(0), 6);
        Assert.Equal(1.01, ChaosVibeTrail.GlowScale(90), 6);       // midpoint of the ease
        Assert.Equal(1.12, ChaosVibeTrail.GlowScale(180), 6);
        Assert.Equal(1.01, ChaosVibeTrail.GlowScale(270), 6);      // auto-reverse
        Assert.Equal(0.9, ChaosVibeTrail.GlowScale(360), 6);
        Assert.True(ChaosVibeTrail.GlowScale(45) < 0.9 + 0.22 * 0.25);   // eased, not linear
    }
}
