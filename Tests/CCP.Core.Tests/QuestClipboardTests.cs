using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace CCP.Core.Tests;

public sealed class QuestClipboardTests
{
    [Theory]
    [InlineData(5, 7, 5, 3)]
    [InlineData(2, 9, 5, 0)]
    [InlineData(9, 9, 5, 5)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(-2, 3, 3, 0)]
    [InlineData(8, 3, 3, 3)]
    public void VisibleSlipsPreserveTheOpenCount(int done, int total, int shown, int checkedCount) =>
        Assert.Equal((shown, checkedCount), QuestClipboard.Fit(done, total));

    [Fact]
    public void PayloadDefaultsAndClampsInvalidCounts()
    {
        Assert.Equal((0, 3), QuestClipboard.Data(null));
        Assert.Equal((0, 0), QuestClipboard.Data(new Dictionary<string, int> { ["total"] = -2, ["done"] = 8 }));
        Assert.Equal((2, 2), QuestClipboard.Data(new Dictionary<string, int> { ["total"] = 2, ["done"] = 9 }));
    }

    [Fact]
    public void StampCyclesOnlyThroughCompletedSlips()
    {
        foreach (double t in new[] { 0.0, 0.4, 1.39 })
            for (int done = 1; done <= 5; done++) Assert.Equal(-1, QuestClipboard.StampTarget(t, done));
        for (int loop = 0; loop < 20; loop++)
        {
            double t = QuestClipboard.LoopStart + loop * QuestClipboard.LoopSeconds + 0.01;
            Assert.Equal(-1, QuestClipboard.StampTarget(t, 0));
            for (int done = 1; done <= 5; done++) Assert.Equal(loop % done, QuestClipboard.StampTarget(t, done));
        }
    }

    [Fact]
    public void BoardGrowsAndAllSlipRowsFit()
    {
        double prev = 0;
        for (int n = 1; n <= 5; n++)
        {
            double bh = QuestClipboard.BoardHeight(340, n);
            Assert.True(bh >= prev);
            Assert.InRange(bh, 170, 340 * 0.86);
            prev = bh;
        }
        Assert.Equal(QuestClipboard.BoardHeight(340, 1), QuestClipboard.BoardHeight(340, 0));
        Assert.Equal(QuestClipboard.BoardHeight(340, 5), QuestClipboard.BoardHeight(340, 8));
    }
    [Theory]
    [InlineData(0.6, -150, 0, 10, 1, 1)]
    [InlineData(0.9, -32.925, 0.875, 1.25, 1, 1)]
    [InlineData(1.5, -22.5, 1, -4, 0.95, 1.08)]
    [InlineData(1.56, -16.875, 1, -2, 0.95, 1.08)]
    [InlineData(1.62, 0, 1, 0, 1.22, 0.78)]
    [InlineData(2.75, -131.25, 0.125, 7, 1, 1)]
    public void StampFollowsWpfBeats(double p, double y, double alpha, double tilt, double sx, double sy)
    {
        var f = QuestClipboard.StampFrame(p, 18, 200);
        Assert.Equal(y, f.Y, 6); Assert.Equal(alpha, f.Alpha, 6); Assert.Equal(tilt, f.Tilt, 6);
        Assert.Equal(sx, f.Sx, 6); Assert.Equal(sy, f.Sy, 6);
    }

    [Fact]
    public void StampStretchesDuringWindUp()
    {
        var f = QuestClipboard.StampFrame(1.35, 18, 200);
        Assert.Equal(-2.828427, f.Tilt, 6);
        Assert.Equal(0.971716, f.Sx, 6); Assert.Equal(1.042426, f.Sy, 6);
    }
}
