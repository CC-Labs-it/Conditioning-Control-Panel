using System;
using Avalonia;
using ConditioningControlPanel.Avalonia.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionSwapTests
{
    [Fact]
    public void PartnerDistanceUsesCentresAndIncludesTheBoundary()
    {
        var origin = new Rect(0, 0, 100, 40);
        Assert.Equal(220, SwapPartnerRule.Score(origin, new Rect(220, 0, 100, 40), false, false));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(221, 0, 100, 40), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(7, 0, 100, 40), false, false)));
        Assert.Equal(8, SwapPartnerRule.Score(origin, new Rect(8, 0, 100, 40), false, false));
        Assert.Equal(100, SwapPartnerRule.Score(origin, new Rect(80, 0, 140, 40), false, false));
    }

    [Fact]
    public void PartnerSizeChecksEachDimensionInBothDirections()
    {
        var origin = new Rect(0, 0, 100, 50);
        Assert.True(double.IsFinite(SwapPartnerRule.Score(origin, new Rect(100, 0, 160, 80), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(100, 0, 161, 50), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(100, 0, 100, 81), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(new Rect(0, 0, 161, 50), new Rect(100, 0, 100, 50), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(100, 0, 0, 50), false, false)));
    }

    [Fact]
    public void StartStopCandidateBeatsNearerOrdinaryCandidate()
    {
        var origin = new Rect(0, 0, 50, 40);
        var near = new Rect(50, 0, 50, 40);
        var far = new Rect(200, 0, 50, 40);
        Assert.True(SwapPartnerRule.Score(origin, far, true, true) < SwapPartnerRule.Score(origin, near, true, false));
        Assert.True(SwapPartnerRule.Score(origin, near, false, false) < SwapPartnerRule.Score(origin, far, false, true));
    }
}
