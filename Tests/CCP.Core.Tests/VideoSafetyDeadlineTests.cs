using System;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VideoSafetyDeadlineTests
{
    [Theory]
    [InlineData(10, 15)]
    [InlineData(0, 600)]
    public void PlaybackHoldsDoNotSuspendTheSafetyDeadline(double duration, double timeout)
    {
        var start = DateTime.UnixEpoch;
        var deadline = new VideoSafetyDeadline();
        deadline.Arm(start, duration);
        Assert.False(deadline.Expired(start.AddSeconds(timeout - 0.01)));
        Assert.True(deadline.Expired(start.AddSeconds(timeout)));
        deadline.Reset();
        Assert.False(deadline.Expired(start.AddDays(1)));
    }

    [Fact]
    public void GracePausePreservesOnlyTheUnspentTimeout()
    {
        var start = DateTime.UnixEpoch;
        var deadline = new VideoSafetyDeadline();
        deadline.Arm(start, 10);
        deadline.PauseForGrace(start.AddSeconds(3));
        Assert.False(deadline.Expired(start.AddSeconds(63)));
        deadline.ResumeFromGrace(start.AddSeconds(63));
        Assert.False(deadline.Expired(start.AddSeconds(74.99)));
        Assert.True(deadline.Expired(start.AddSeconds(75)));
    }
}
