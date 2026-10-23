using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SeasonRecapMetricsTests
{
    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 100, 0)]
    [InlineData(1, 1000, 1)]
    [InlineData(2, 300, 1)]
    [InlineData(2, 10, 20)]
    [InlineData(1, 3, 34)]
    [InlineData(100, 100, 99)]
    [InlineData(101, 100, 99)]
    public void PercentileKeepsWpfUnrankedRoundingAndBounds(int rank, int total, int expected) =>
        Assert.Equal(expected, SeasonRecapMetrics.PercentileFor(rank, total));
}
