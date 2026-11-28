using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class NotificationHistoryCoreTests
{
    [Fact]
    public void RecordsNewestFirstWithCapacityAndIndependentSnapshots()
    {
        var history = new NotificationHistoryBuffer<int>();
        var at = new DateTime(2026, 11, 22, 10, 0, 0);
        var changes = 0;
        history.Changed += () => changes++;
        history.Record(at, 0, null);
        history.Record(at, 0, "  ");
        Assert.Equal(0, changes);
        for (var i = 0; i < 70; i++) history.Record(at.AddSeconds(i), i, $" n{i} ");
        var snapshot = history.Snapshot();
        Assert.Equal(50, history.Count);
        Assert.Equal(70, changes);
        Assert.Equal("n69", snapshot[0].Text);
        Assert.Equal(69, snapshot[0].Type);
        Assert.Equal(at.AddSeconds(69), snapshot[0].At);
        Assert.Equal("n20", snapshot[^1].Text);
        history.Record(at, 0, "later");
        Assert.Equal("n69", snapshot[0].Text);
    }
}
