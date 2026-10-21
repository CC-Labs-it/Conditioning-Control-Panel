using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ProgramClockTests
{
    [Theory]
    [InlineData(-1, "0:00")]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(90123, "25:02:03")]
    public void ProgramClockUsesWpfShapeAndClampsNegative(int seconds, string expected) =>
        Assert.Equal(expected, SessionClockLabel.ProgramClock(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(-2, 20, 0)]
    [InlineData(0, 20, 0)]
    [InlineData(5, 20, 25)]
    [InlineData(20, 20, 100)]
    [InlineData(21, 20, 100)]
    [InlineData(5, 0, 100)]
    public void ProgressIsClamped(double elapsedMinutes, double durationMinutes, double expected) =>
        Assert.Equal(expected, SessionClockLabel.ProgramProgress(TimeSpan.FromMinutes(elapsedMinutes), durationMinutes));
}
