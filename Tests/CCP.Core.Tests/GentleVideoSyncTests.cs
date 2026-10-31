using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public class GentleVideoSyncTests
{
    [Theory]
    [InlineData(false, 0.25)]
    [InlineData(true, 0.25)]
    [InlineData(true, 0.75)]
    [InlineData(true, 1)]
    public void Videos_keep_normal_rate(bool on, double speed)
        => Assert.Equal(1f, GentleWindowRules.PlaybackRate(on, speed));

    [Theory]
    [InlineData(true, MotionLevel.Full, 0.25, 0.5)]
    [InlineData(true, MotionLevel.Full, 0.75, 0.75)]
    [InlineData(true, MotionLevel.Reduced, 0.75, 0)]
    [InlineData(false, MotionLevel.Full, 0.75, 0)]
    public void Chess_intro_only_slows_when_gentle_is_the_only_brake(bool gentle, MotionLevel level, double speed, double expected)
    {
        var rate = PbpWatchRules.GentleIntroSpeed(gentle, level, speed);
        var frame = PbpWatchRules.SettingsFrame(0, gentle, null, null, false, null, rate);
        Assert.Equal(expected, (double)frame["gentleIntroSpeed"]!);
    }
}
