using System.Collections.Generic;
using ConditioningControlPanel.Avalonia;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The --overlay-bench verdict (compositor decision gate): pure, no display.</summary>
public sealed class OverlayBenchMetricsTests
{
    private static FrameTrack Track(params double[] at)
    {
        var t = new FrameTrack("w");
        foreach (var ms in at) t.Add(ms);
        return t;
    }

    [Fact]
    public void SmoothRunPasses()
    {
        var lat = new List<double> { 1, 2, 3, 99.9 };
        Assert.Empty(OverlayBenchMetrics.Failures(lat, new[] { Track(0, 16.7, 33.4) }));
    }

    [Fact]
    public void LatencyOverOneHundredMsFails()
    {
        var fails = OverlayBenchMetrics.Failures(new List<double> { 1, 100.1 }, new[] { Track(0, 16.7) });
        Assert.Single(fails);
        Assert.Contains("max UI latency", fails[0]);
    }

    [Fact]
    public void UiStallAndFrameStallAreBothReported()
    {
        var fails = OverlayBenchMetrics.Failures(new List<double> { 1500 }, new[] { Track(0, 16, 1016) });
        Assert.Contains(fails, f => f.StartsWith("UI-thread stall"));
        Assert.Contains(fails, f => f.StartsWith("frame stall"));
    }

    [Fact]
    public void FrameStallAloneFailsEvenWithAQuietUiThread()
    {
        var fails = OverlayBenchMetrics.Failures(new List<double> { 2 }, new[] { Track(0, 1700) });
        Assert.Equal(new[] { "frame stall 1700 ms in w" }, fails);
    }

    [Fact]
    public void ARunThatMeasuredNothingFails()
    {
        var fails = OverlayBenchMetrics.Failures(new List<double>(), new[] { Track(0) });
        Assert.Contains("no UI-thread latency samples", fails);
        Assert.Contains("no overlay window drew two frames", fails);
    }

    [Fact]
    public void MissedFramesAndPercentile()
    {
        var t = Track(0, 16.7, 50, 150);   // gaps 16.7 (0), 33.3 (1), 100 (5)
        Assert.Equal(4, t.Frames);
        Assert.Equal(6, t.Missed);
        Assert.Equal(100.0, t.MaxGapMs, 3);
        var xs = new List<double>();
        for (var i = 1; i <= 100; i++) xs.Add(i);
        Assert.Equal(95.0, OverlayBenchMetrics.Percentile(xs, 95));
        Assert.Equal(0.0, OverlayBenchMetrics.Percentile(new List<double>(), 95));
    }
}
