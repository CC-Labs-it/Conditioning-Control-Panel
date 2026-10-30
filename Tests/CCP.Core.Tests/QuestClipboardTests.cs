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
}
