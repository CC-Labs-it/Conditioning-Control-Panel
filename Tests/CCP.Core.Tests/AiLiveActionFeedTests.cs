using System;
using System.Collections.ObjectModel;
using ConditioningControlPanel.Services.Commands;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AiLiveActionFeedTests
{
    [Fact]
    public void KeepsTheLastThirtyInTimeOrderAndTheWpfTimestamp()
    {
        var feed = new ObservableCollection<string>();
        var time = new DateTime(2026, 10, 14, 14, 3, 5);
        for (var i = 0; i < 35; i++) AiLiveActionFeed.Append(feed, "action " + i, time);
        Assert.Equal(30, feed.Count);
        Assert.Equal("[14:03:05] action 5", feed[0]);
        Assert.Equal("[14:03:05] action 34", feed[^1]);
    }

    [Theory]
    [InlineData("[14:03:05] ⛔ Follow-up blocked", "[14:03:05]", "⛔ Follow-up blocked")]
    [InlineData("plain action", "", "plain action")]
    public void TimestampIsSeparateFromTheActionIcon(string line, string timestamp, string action)
        => Assert.Equal((timestamp, action), AiLiveActionFeed.Parts(line));
}
