using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChromeBannerSheenTests
{
    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, true)]
    [InlineData(MotionLevel.Off, false)]
    public void InteractionCueRetainsReducedMotion(MotionLevel level, bool expected)
        => Assert.Equal(expected, ChromeBannerSheen.CanSweep(true, level, true, DateTime.UtcNow, DateTime.MinValue));

    [Theory]
    [InlineData(19.999, false, false)]
    [InlineData(20, false, true)]
    [InlineData(4, true, true)]
    public void RotationThrottlesButAnnouncementBypasses(double gap, bool force, bool expected)
    {
        var last = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, ChromeBannerSheen.CanSweep(true, MotionLevel.Full, force, last.AddSeconds(gap), last));
    }

    [Fact]
    public void ForcedAnnouncementNeverBypassesWindowGate()
        => Assert.False(ChromeBannerSheen.CanSweep(false, MotionLevel.Full, true, DateTime.UtcNow, DateTime.MinValue));
}
