using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Review round on #5582: the drop import card and the weekly video quest line.</summary>
public class DropImportGateTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_report_that_lands_after_the_import_ended_is_ignored()
    {
        var gate = new DropImportGate();
        var run = gate.Begin();
        Assert.True(gate.Busy);
        Assert.True(gate.Accepts(run));
        gate.End(run);
        Assert.False(gate.Busy);
        Assert.False(gate.Accepts(run)); // the late report never puts the card back up
        Assert.False(gate.Busy);
    }

    [Fact]
    public void A_previous_runs_report_never_draws_on_the_next_import()
    {
        var gate = new DropImportGate();
        var first = gate.Begin();
        gate.End(first);
        var second = gate.Begin();
        Assert.False(gate.Accepts(first));
        Assert.True(gate.Accepts(second));
        gate.End(first); // a stale end leaves the running import busy
        Assert.True(gate.Busy);
        gate.End(second);
        Assert.False(gate.Busy);
    }

    [Fact]
    public void A_refused_drop_says_why_at_most_once_every_few_seconds()
    {
        var gate = new DropImportGate();
        Assert.False(gate.ShouldSayBusy(T0)); // not busy: nothing refused
        gate.Begin();
        Assert.True(gate.ShouldSayBusy(T0));
        Assert.False(gate.ShouldSayBusy(T0.AddSeconds(1)));
        Assert.True(gate.ShouldSayBusy(T0 + DropImportGate.BusyNoticeGap));
    }

    [Fact]
    public void The_weekly_line_takes_the_localized_text_and_falls_back_to_the_hardcoded_one()
    {
        Assert.Equal("Guarda 90 minuti di video", QuestVideoCopy.LocalizedOr("Guarda 90 minuti di video", "Watch 90 minutes of video"));
        Assert.Equal("Watch 90 minutes of video", QuestVideoCopy.LocalizedOr("quest_marathon_trance_w_desc", "Watch 90 minutes of video"));
        Assert.Equal("Watch 90 minutes of video", QuestVideoCopy.LocalizedOr("", "Watch 90 minutes of video"));
        Assert.Equal("Watch 90 minutes of video", QuestVideoCopy.LocalizedOr(null, "Watch 90 minutes of video"));
    }
}
