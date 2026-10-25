using System;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ProgramSessionProgressTests
{
    [Theory]
    [InlineData(-1, "0:00")]
    [InlineData(0, "0:00")]
    [InlineData(59, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(90061, "25:01:01")]
    public void ClockPreservesWpfsTotalHoursAndMinuteShape(int seconds, string expected) =>
        Assert.Equal(expected, ProgramSessionProgress.Clock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(-10, 10, 0, 0)]
    [InlineData(300, 10, 300, 50)]
    [InlineData(900, 10, 600, 100)]
    [InlineData(30, 0, 30, 100)]   // zero-length reads full (WPF ProgressPercent, P28)
    public void SnapshotBoundsElapsedAndUsesAtLeastOneMinute(int seconds, int minutes, int boundedSeconds, double percent)
    {
        var progress = ProgramSessionProgress.From(TimeSpan.FromSeconds(seconds), minutes);
        Assert.Equal(TimeSpan.FromSeconds(boundedSeconds), progress.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(Math.Max(1, minutes)), progress.Total);
        Assert.Equal(percent, progress.Percent);
    }

    // Retargeted from the lane's SessionClockLabel.ProgramProgress cases: the bar fill alone.
    [Theory]
    [InlineData(-2, 20, 0)]
    [InlineData(0, 20, 0)]
    [InlineData(5, 20, 25)]
    [InlineData(20, 20, 100)]
    [InlineData(21, 20, 100)]
    [InlineData(5, 0, 100)]
    public void ProgressIsClamped(double elapsedMinutes, int durationMinutes, double expected) =>
        Assert.Equal(expected, ProgramSessionProgress.From(TimeSpan.FromMinutes(elapsedMinutes), durationMinutes).Percent);

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(90123, "25:02:03")]
    public void ClockKeepsLaneCases(int seconds, string expected) =>
        Assert.Equal(expected, ProgramSessionProgress.Clock(TimeSpan.FromSeconds(seconds)));
}
