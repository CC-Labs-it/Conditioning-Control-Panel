using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1474: Firmware Install cycle 12's optional "accept 25 remote commands" stopped
/// counting the moment the required override run closed the day. Optional tasks now keep
/// counting for the rest of that day; the day itself is never reopened or judged twice.
/// </summary>
public class ProgramOptionalAfterCloseTests
{
    private static ProgramDay Cycle12() => BuiltInPrograms.FirmwareInstall().GetDay(12)!;

    private static ProgramDayRecord ClosedRecord() => new()
    {
        DayIndex = 12,
        SessionCompleted = true,
        DayCompleted = true,
        CompletedTaskIds = { "c12_override" }
    };

    [Fact]
    public void Optional_task_keeps_counting_after_the_day_closes()
    {
        var day = Cycle12();
        var record = ClosedRecord();

        var moved = ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Remote, 10, _ => false, out var finished);

        Assert.True(moved);
        Assert.Equal(10, record.TaskProgress["c12_remote_optional"]);
        Assert.Empty(finished);
        Assert.True(record.DayCompleted);
    }

    [Fact]
    public void Optional_task_finishes_once_after_close()
    {
        var day = Cycle12();
        var record = ClosedRecord();

        ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Remote, 24, _ => false, out _);
        ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Remote, 1, _ => false, out var finished);
        var again = ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Remote, 5, _ => false, out var second);

        Assert.Equal(new[] { "c12_remote_optional" }, finished);
        Assert.False(again);
        Assert.Empty(second);
        Assert.Single(record.CompletedTaskIds, id => id == "c12_remote_optional");
    }

    [Fact]
    public void Closed_day_credits_no_required_task()
    {
        var day = Cycle12();
        var record = ClosedRecord();
        record.CompletedTaskIds.Clear(); // even an unticked required task stays shut

        var moved = ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Autonomy, 5, _ => false, out _);

        Assert.False(moved);
        Assert.False(record.TaskProgress.ContainsKey("c12_override"));
    }

    [Fact]
    public void Blocked_optional_task_does_not_count()
    {
        var day = Cycle12();
        var record = ClosedRecord();

        var moved = ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Remote, 5,
            t => t.Id == "c12_remote_optional", out _);

        Assert.False(moved);
    }

    [Fact]
    public void Open_day_counts_required_and_optional_as_before()
    {
        var day = Cycle12();
        var record = new ProgramDayRecord { DayIndex = 12 };

        ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Autonomy, 40, _ => false, out var finished);
        ProgramService.ApplyVerifiedProgress(day, record, QuestCategory.Remote, 3, _ => false, out _);

        Assert.Contains("c12_override", finished);
        Assert.Equal(3, record.TaskProgress["c12_remote_optional"]);
        Assert.False(record.DayCompleted);
        Assert.True(day.Tasks.Single(t => t.Id == "c12_remote_optional").Optional);
    }
}
