using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SherpaWakeCalibrationTests
{
    [Fact]
    public void SelectsStrictestReliableQuietThreshold()
    {
        var result = SherpaWakeService.SelectCalibrationThreshold(5,
            new[] { 2, 4, 4, 5, 5, 5, 5, 5 },
            new[] { false, true, false, false, false, false, false, false });
        Assert.Equal((0.20, 4, false), result);
    }

    [Fact]
    public void FallsBackToRecallWhenAllReliableThresholdsFireOnAmbient()
    {
        var result = SherpaWakeService.SelectCalibrationThreshold(5,
            new[] { 1, 4, 5, 5, 5, 5, 5, 5 },
            new[] { false, true, true, true, true, true, true, true });
        Assert.Equal((0.24, 4, true), result);
    }

    [Fact]
    public void BestEffortRequiresAtLeastTwoCatchesAndKeepsStrictestTie()
    {
        var ambient = new[] { false, true, false, false, false, false, false, false };
        Assert.Equal((0.24, 3, true), SherpaWakeService.SelectCalibrationThreshold(5,
            new[] { 1, 3, 3, 2, 2, 2, 2, 2 }, ambient));
        Assert.Null(SherpaWakeService.SelectCalibrationThreshold(5,
            new[] { 0, 1, 1, 1, 1, 1, 1, 1 }, ambient));
    }
}
