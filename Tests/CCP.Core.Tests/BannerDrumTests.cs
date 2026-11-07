using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public class BannerDrumTests
{
    [Theory]
    [InlineData(MotionLevel.Off)]
    [InlineData(MotionLevel.Reduced)]
    public void LowerMotionKeepsBothFacesAtRest(MotionLevel level)
    {
        Assert.Equal((0d, 1d), BannerDrum.Face(level, 0.2, true));
        Assert.Equal((0d, 1d), BannerDrum.Face(level, 0.2, false));
    }

    [Fact]
    public void FullRollClimbsAndSquashesThenReleasesBothFaces()
    {
        Assert.Equal((13d, 0.3d), BannerDrum.Face(MotionLevel.Full, 0, true));
        Assert.Equal((0d, 1d), BannerDrum.Face(MotionLevel.Full, 0, false));
        var departing = BannerDrum.Face(MotionLevel.Full, 0.25, false);
        Assert.Equal(-3.25, departing.Y);
        Assert.Equal(0.825, departing.Scale, 6);
        var arriving = BannerDrum.Face(MotionLevel.Full, 0.25, true);
        Assert.Equal(-0.325, arriving.Y, 6);
        Assert.Equal(1.0175, arriving.Scale, 6);
        foreach (bool incoming in new[] { true, false })
        {
            Assert.Equal((0d, 1d), BannerDrum.Face(MotionLevel.Full, 0.5, incoming));
            Assert.Equal((0d, 1d), BannerDrum.Face(MotionLevel.Full, 1, incoming));
        }
    }
}
