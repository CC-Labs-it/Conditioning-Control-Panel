using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class QuestCompletionLogTests
{
    [Theory]
    [InlineData("2026-11-13T00:30:00+01:00", "2026-11-12")]
    [InlineData("2026-11-12T23:30:00-08:00", "2026-11-13")]
    [InlineData("2026-11-13T00:00:00Z", "2026-11-13")]
    public void NewReceiptsUseTheUtcCompletionDay(string instant, string expected)
    {
        var progress = new QuestProgress();
        QuestService.RecordQuestInLog(progress, DateTimeOffset.Parse(instant), "spiral_sink_d");
        var receipt = Assert.Single(progress.QuestCompletionLog);
        Assert.Equal(expected, receipt.D);
        Assert.Equal("spiral_sink_d", receipt.Q);
    }

    [Fact]
    public void RecordingDeduplicatesUtcReceiptsAndPreservesLocalCalendarAndHistory()
    {
        var localDay = new DateTime(2026, 11, 13);
        var old = new QuestLogEntry("2026-11-10", "old_d");
        var progress = new QuestProgress
        {
            DailyQuestCompletionDates = new List<DateTime> { localDay },
            QuestCompletionLog = new List<QuestLogEntry> { old }
        };
        QuestService.RecordQuestInLog(progress, DateTimeOffset.Parse("2026-11-13T00:30:00+01:00"), "spiral_sink_d");
        QuestService.RecordQuestInLog(progress, DateTimeOffset.Parse("2026-11-12T23:30:00Z"), "spiral_sink_d");
        Assert.Equal(2, progress.QuestCompletionLog.Count);
        Assert.Same(old, progress.QuestCompletionLog[0]);
        Assert.Equal(localDay, Assert.Single(progress.DailyQuestCompletionDates));
        Assert.Equal("2026-11-12", progress.QuestCompletionLog.Last().D);
    }
}
