using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// BUG-7NAKK79CZ2: a 25-minute web video played under a 20m 3s max video length.
public sealed class VideoMaxCapTests
{
    [Theory]
    [InlineData(0, 100, null)]        // no cap set
    [InlineData(1203, 0, 1203.0)]     // fresh clip: the whole cap
    [InlineData(1203, 1000, 203.0)]   // a replay/fallback continues the budget
    [InlineData(1203, 1500, 0.0)]     // already past it: end now, never negative
    public void CapLeftContinuesTheClipBudget(int max, double spent, double? expected)
        => Assert.Equal(expected, VideoMaxCap.CapLeft(max, spent));

    [Fact]
    public void AppTakeoverEndsAtTheUsersMaxNotAtTheClip()
        // 25-min Hypnotube clip, 20m 3s max, autonomy takeover: stop at the cap, not clip + 15 s.
        => Assert.Equal(1203.0, VideoMaxCap.TakeoverStopAfter(1500, 0, 1203, userStarted: false));

    [Fact]
    public void AutoAdvanceCannotRestartTheCap()
        // 600 s into the takeover the site advances to a 25-min clip: what is left of the cap.
        => Assert.Equal(603.0, VideoMaxCap.TakeoverStopAfter(1500, 600, 1203, userStarted: false));

    [Fact]
    public void UnknownLengthStillCappedForAppTakeover()
    {
        Assert.Equal(1203.0, VideoMaxCap.TakeoverStopAfter(0, 0, 1203, userStarted: false));
        Assert.Null(VideoMaxCap.TakeoverStopAfter(0, 0, 0, userStarted: false));   // no cap: heartbeat governs, as before
    }

    [Fact]
    public void ShortClipStillEndsAtClipPlusGrace()
        => Assert.Equal(315.0, VideoMaxCap.TakeoverStopAfter(300, 0, 1203, userStarted: false));

    [Fact]
    public void UserStartedTakeoverIgnoresTheCap()
    {
        Assert.Equal(1515.0, VideoMaxCap.TakeoverStopAfter(1500, 0, 1203, userStarted: true));
        Assert.Null(VideoMaxCap.TakeoverStopAfter(0, 0, 1203, userStarted: true));
        Assert.Equal(VideoMaxCap.TakeoverMaxSeconds, VideoMaxCap.TakeoverStopAfter(99999, 0, 0, userStarted: true));
    }

    [Fact]
    public void MandatoryGuardCountsCapSpentBeforeAReplay()
    {
        Assert.Null(MandatoryVideoScheduler.Guard(3, 2, true, true, 600, 0, 8));
        Assert.Equal("max length", MandatoryVideoScheduler.Guard(3, 2, true, true, 600, 0, 8, capSpentBefore: 7));
        Assert.Null(MandatoryVideoScheduler.Guard(3, 2, true, true, 600, 0, 0, capSpentBefore: 7));
    }
}
