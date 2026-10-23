using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class VideoPlaybackClockTests
{
    private static VideoPlaybackClock Started()
    {
        var clock = new VideoPlaybackClock(); clock.Observe(0, 0); return clock;
    }
    [Fact] public void PauseAndStallConsumeNothing()
    {
        var c = Started(); c.Observe(0, 10000); c.Observe(250, 10250);
        Assert.Equal(250, c.PlayedMs);
    }
    [Fact] public void ForwardJumpCannotChargeSkippedIntroduction()
    {
        var c = Started(); c.Observe(960000, 250);
        Assert.Equal(250, c.PlayedMs);
    }
    [Fact] public void RewindDoesNotRefundAndReplayCounts()
    {
        var c = Started(); c.Observe(1000, 1000); c.Observe(0, 1250); c.Observe(250, 1500);
        Assert.Equal(1250, c.PlayedMs);
    }
    [Fact] public void ExplicitSeekResetsPositionButPreservesCredit()
    {
        var c = Started(); c.Observe(500, 500); c.ResetPosition();
        c.Observe(960000, 750); c.Observe(960250, 1000);
        Assert.Equal(750, c.PlayedMs);
    }
    [Fact] public void CoarsePositionUpdatesRetainElapsedAllowance()
    {
        var c = Started(); c.Observe(0, 250); c.Observe(0, 500); c.Observe(750, 750);
        Assert.Equal(750, c.PlayedMs);
    }
}
