using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public class NavCoinLawTests
{
    [Fact]
    public void ReleasePassesTheTargetThenSettlesInEitherDirection()
    {
        Assert.Equal(-3, NavCoinLaw.Travel(2, -2, 0.6, true));
        Assert.Equal(2, NavCoinLaw.Travel(-2, 1, 0.6, true));
        Assert.Equal(-2, NavCoinLaw.Travel(2, -2, 1, true));
        Assert.Equal(1, NavCoinLaw.Travel(1, 1, 0.6, true));
        Assert.Equal(2, NavCoinLaw.Travel(2, -2, 0, true));
    }
    [Fact]
    public void TiltIsClampedAndRespectsTheMotionAndTierGates()
    {
        Assert.Equal(-DepthLaw.TiltDegrees, NavCoinLaw.Tilt(3, 2));
        Assert.Equal(DepthLaw.TiltDegrees, NavCoinLaw.Tilt(1, -1));
        Assert.False(NavCoinLaw.Tilts(true, MotionLevel.Full, PerformanceTier.Quality));
        Assert.False(NavCoinLaw.Tilts(false, MotionLevel.Off, PerformanceTier.Quality));
        Assert.False(NavCoinLaw.Tilts(false, MotionLevel.Full, PerformanceTier.Performance));
        Assert.True(NavCoinLaw.Tilts(false, MotionLevel.Reduced, PerformanceTier.Quality));
    }
}
