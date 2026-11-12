using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class GazeDriftCorrectionTests
{
    [Fact]
    public void NormalFoldRejectsSmallAndDistantClicksAndClampsOffset()
    {
        Assert.Null(GazeDriftCorrection.Fold(14, 0, 0, 0));
        Assert.Null(GazeDriftCorrection.Fold(221, 0, 0, 0));
        Assert.Null(GazeDriftCorrection.Fold(double.NaN, 0, 0, 0));
        var result = GazeDriftCorrection.Fold(100, -100, 295, -295)!.Value;
        Assert.Equal(300, result.Dx);
        Assert.Equal(-300, result.Dy);
        Assert.Equal(15, GazeDriftCorrection.Fold(100, 0, 0, 0)!.Value.Dx, 6);
    }

    [Fact]
    public void FastFoldRampsGainAndCapBackToNormal()
    {
        Assert.Equal(45, GazeDriftCorrection.Fold(100, 0, 0, 0, 0)!.Value.Dx, 6);
        Assert.Equal(30, GazeDriftCorrection.Fold(100, 0, 0, 0, 0.5)!.Value.Dx, 6);
        Assert.NotNull(GazeDriftCorrection.Fold(290, 0, 0, 0, 0));
        Assert.Null(GazeDriftCorrection.Fold(290, 0, 0, 0, 0.5));
        Assert.Equal(15, GazeDriftCorrection.Fold(100, 0, 0, 0, 1)!.Value.Dx, 6);
    }
}
