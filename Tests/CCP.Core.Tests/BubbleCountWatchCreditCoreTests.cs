using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class BubbleCountWatchCreditCoreTests
{
    [Fact]
    public void CompletionAndEarlyCloseUseElapsedTimeAndCreditOnlyOnce()
    {
        var complete = new BubbleCountWatchCredit();
        complete.MarkPlaying(100);
        complete.NotePosition(45);
        Assert.Equal(42, complete.Take(142, 45));
        Assert.Equal(0, complete.Take(145, 45));
        var early = new BubbleCountWatchCredit();
        early.MarkPlaying(10);
        Assert.Equal(12, early.Take(22, 60));
    }

    [Fact]
    public void StallCapsAtFurthestPositionAndNeverExceedsWallOrClip()
    {
        var watch = new BubbleCountWatchCredit();
        watch.MarkPlaying(0); watch.NotePosition(10); watch.NotePosition(9);
        Assert.Equal(10, watch.Take(65, 60));
        var seek = new BubbleCountWatchCredit();
        seek.MarkPlaying(0); seek.NotePosition(50);
        Assert.Equal(20, seek.Take(20, 60));
        var browser = new BubbleCountWatchCredit();
        browser.MarkPlaying(0);
        Assert.Equal(30, browser.Take(95, 30));
        Assert.Equal(0, new BubbleCountWatchCredit().Take(30, 30));
    }
}
