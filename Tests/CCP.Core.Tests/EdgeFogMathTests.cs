using System;
using ConditioningControlPanel.Services.UI.EdgeEffects;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core EdgeFogMath, pinned as WPF FogPolish11Tests.cs:81/:101 and FogDustTests.cs pin it.</summary>
public sealed class EdgeFogMathTests
{
    [Fact]
    public void TheWholeRingStaysUnderSixtyLiveElements()
    {
        int fog = 0, reduced = 0;
        foreach (bool longSide in new[] { true, true, false, false })
        {
            fog += EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), 60, false)
                 + EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), 60, false);
            reduced += EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), 60, true)
                     + EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), 60, true);
        }
        Assert.Equal(34, fog);
        Assert.True(fog + 4 * EdgeDriftMath.MaxPerStrip < 60);
        Assert.InRange(reduced, 15, 19);
        Assert.Equal(0, EdgeFogMath.Target(4, 0, false));
        Assert.Equal(3, EdgeFogMath.Target(5, 24, false));
    }

    [Fact]
    public void ThePuffEnvelopeFadesInHoldsAndFadesOut()
    {
        Assert.Equal(0, EdgeFogMath.Envelope(0, 6));
        Assert.Equal(0, EdgeFogMath.Envelope(6, 6));
        Assert.Equal(1, EdgeFogMath.Envelope(3, 6));
        Assert.InRange(EdgeFogMath.Envelope(0.6, 6), 0.05, 0.6);
        Assert.Equal(EdgeFogMath.Envelope(1, 6), EdgeFogMath.Envelope(5, 6), 9);
        Assert.Equal(EdgeFogMath.SmallAlphaMax, EdgeFogMath.Alpha(0.22, 3, 6, 1.5), 9);
        double deep = EdgeFogMath.MaxDepth(EdgeFogMath.BigSizeMaxPx, EdgeFogMath.BreathePxMax);
        Assert.True(deep + EdgeFogMath.BreathePxMax + EdgeFogMath.BigSizeMaxPx / 2 <= EdgeFogMath.StripPx);
    }

    [Fact]
    public void PositionsRunClockwiseInsideEachStrip()
    {
        Assert.Equal((30d, 7d), EdgeFogMath.Position(EdgeSide.Top, 30, 7, 200, 56));
        Assert.Equal((49d, 30d), EdgeFogMath.Position(EdgeSide.Right, 30, 7, 56, 200));
        Assert.Equal((170d, 49d), EdgeFogMath.Position(EdgeSide.Bottom, 30, 7, 200, 56));
        Assert.Equal((7d, 170d), EdgeFogMath.Position(EdgeSide.Left, 30, 7, 56, 200));
        Assert.Equal(200, EdgeFogMath.Length(EdgeSide.Right, 56, 200));
    }

    [Theory]
    [InlineData(0.0, 2.0), InlineData(0.5, 7.0)]
    [InlineData(1.0, 7.0), InlineData(1.0, 2.0)]
    public void ASpeckWandersInsideTheStripAndHugsTheFrame(double u, double wander)
    {
        double depth = EdgeFogMath.DustDepth(u, wander);
        Assert.True(depth - wander >= 0.9, $"outer reach {depth - wander}");
        Assert.True(depth + wander <= EdgeFogMath.StripPx - 1.9, $"inner reach {depth + wander}");
        Assert.True(EdgeFogMath.DustDepth(1.0, EdgeFogMath.DustWanderPxMax) + EdgeFogMath.DustWanderPxMax <= 35);
    }

    [Fact]
    public void ASpeckFadesTwinklesAndStaysUnderItsCap()
    {
        Assert.Equal(0, EdgeFogMath.DustAlpha(0.85, 0, 4, 0, 1));
        Assert.Equal(0, EdgeFogMath.DustAlpha(0.85, 4, 4, 0, 1));
        for (double tw = 0; tw < Math.PI * 2; tw += 0.3)
            Assert.InRange(EdgeFogMath.DustAlpha(EdgeFogMath.DustAlphaMax, 2, 4, tw, 1.5), 0, 0.95);
        double lo = EdgeFogMath.DustAlpha(0.6, 2, 4, -Math.PI / 2, 1);
        double hi = EdgeFogMath.DustAlpha(0.6, 2, 4, Math.PI / 2, 1);
        Assert.True(lo > 0.3 && lo < hi, $"lo {lo} hi {hi}");
    }
}
