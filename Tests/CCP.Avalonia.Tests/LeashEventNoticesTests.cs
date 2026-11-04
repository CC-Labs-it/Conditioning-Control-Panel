using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LeashEventNoticesTests
{
    private static LeashEvent Event(string id, LeashEventKind kind = LeashEventKind.Tug) =>
        new(id, kind, new LeashPerson("holder", "Holder", null), DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(LeashEventKind.Tug)]
    [InlineData(LeashEventKind.Reward)]
    [InlineData(LeashEventKind.Punish)]
    [InlineData(LeashEventKind.Assign)]
    public void VisibleNoticeIsShownBeforeSeen(LeashEventKind kind)
    {
        var calls = new List<string>();
        var notices = new LeashEventNotices(e => calls.Add("show:" + e.Id), id => calls.Add("seen:" + id));
        notices.Arrived(Event("one", kind), true);
        Assert.Equal(new[] { "show:one", "seen:one" }, calls);
    }

    [Fact]
    public void HiddenEventsWaitAndRepeatedEventsFlushOnce()
    {
        var calls = new List<string>();
        var notices = new LeashEventNotices(e => calls.Add("show:" + e.Id), id => calls.Add("seen:" + id));
        notices.Arrived(Event("one"), false);
        notices.Arrived(Event("one"), false);
        notices.Arrived(Event("two", LeashEventKind.Assign), false);
        notices.Flush(false);
        Assert.Empty(calls);
        notices.Flush(true);
        notices.Flush(true);
        Assert.Equal(new[] { "show:one", "seen:one", "show:two", "seen:two" }, calls);
    }

    [Fact]
    public void FailedDisplayNeverReportsSeen()
    {
        var seen = new List<string?>();
        var notices = new LeashEventNotices(_ => throw new InvalidOperationException(), seen.Add);
        Assert.Throws<InvalidOperationException>(() => notices.Arrived(Event("one"), true));
        Assert.Empty(seen);
    }

    [Fact]
    public void HiddenQueueDropsOldestWithoutMarkingItSeen()
    {
        var seen = new List<string?>();
        var notices = new LeashEventNotices(_ => { }, seen.Add);
        for (var i = 0; i < 7; i++) notices.Arrived(Event(i.ToString()), false);
        notices.Flush(true);
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, seen);
    }
}
