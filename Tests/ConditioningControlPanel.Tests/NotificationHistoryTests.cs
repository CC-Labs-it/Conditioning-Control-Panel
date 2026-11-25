using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1688: the last toasts can be read again.</summary>
public class NotificationHistoryTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 10, 0, 0);

    [Fact]
    public void Newest_comes_first_with_time_and_type()
    {
        var h = new NotificationHistory();
        h.Record(T0, NotificationType.Info, "first");
        h.Record(T0.AddMinutes(1), NotificationType.Warning, "second");
        var s = h.Snapshot();
        Assert.Equal(2, s.Count);
        Assert.Equal("second", s[0].Text);
        Assert.Equal(NotificationType.Warning, s[0].Type);
        Assert.Equal(T0, s[1].At);
    }

    [Fact]
    public void It_keeps_only_the_last_fifty()
    {
        var h = new NotificationHistory();
        for (var i = 0; i < 70; i++) h.Record(T0.AddSeconds(i), NotificationType.Info, "n" + i);
        var s = h.Snapshot();
        Assert.Equal(NotificationHistory.Capacity, s.Count);
        Assert.Equal("n69", s[0].Text);
        Assert.Equal("n20", s[^1].Text);
    }

    [Fact]
    public void Blank_text_is_not_kept_and_raises_nothing()
    {
        var h = new NotificationHistory();
        var raised = 0;
        h.Changed += () => raised++;
        h.Record(T0, NotificationType.Info, "  ");
        h.Record(T0, NotificationType.Info, null);
        Assert.Equal(0, h.Count);
        Assert.Equal(0, raised);
        h.Record(T0, NotificationType.Error, "x");
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_snapshot_does_not_change_under_the_reader()
    {
        var h = new NotificationHistory();
        h.Record(T0, NotificationType.Info, "a");
        var s = h.Snapshot();
        h.Record(T0, NotificationType.Info, "b");
        Assert.Single(s);
    }
}
