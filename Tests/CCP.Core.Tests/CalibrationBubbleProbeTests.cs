using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CalibrationBubbleProbeTests
{
    [Theory]
    [InlineData(1080, false)]
    [InlineData(720, false)]
    [InlineData(352, false)]
    [InlineData(300, true)]
    public void Layout_PreservesColumnBalanceAndHeaderClearance(double height, bool degenerate)
    {
        var layout = CalibrationBubbleProbe.BuildLayout(1920, height, 114);
        Assert.Equal(4, layout.Points.Length);
        Assert.Equal(degenerate, layout.Degenerate);
        if (height > 352)
        {
            Assert.True(layout.Points[0].Y < layout.Points[3].Y);
            Assert.True(layout.Points[3].Y < layout.Points[1].Y);
            Assert.True(layout.Points[1].Y < layout.Points[2].Y);
        }
        Assert.Equal(460.8, layout.Points[0].X, 8);
        Assert.Equal(1459.2, layout.Points[1].X, 8);
        Assert.Equal(460.8, layout.Points[2].X, 8);
        Assert.Equal(1459.2, layout.Points[3].X, 8);
        Assert.Equal((layout.Points[0].Y + layout.Points[2].Y) / 2,
            (layout.Points[1].Y + layout.Points[3].Y) / 2, 8);
        if (layout.Degenerate) Assert.All(layout.Points, p => Assert.Equal(height / 2, p.Y));
        else Assert.All(layout.Points, p => Assert.InRange(Math.Round(p.Y, 8), 222, height - 130));
    }

    [Fact]
    public void Dwell_UsesTallEllipseAndRollingMean_ThenLeaksInsteadOfResetting()
    {
        var probe = new CalibrationBubbleProbe(0, 0);
        probe.Tick(true, 0, 180); probe.Tick(true, 0, 180);
        Assert.False(probe.OnTarget); // three samples are required
        for (int i = 0; i < 10; i++) probe.Tick(true, 0, 180); // outside a 130 DIP circle, inside ellipse
        Assert.True(probe.OnTarget);
        double held = probe.Progress;
        probe.Tick(true, 130, 180); // one wobble: rolling mean stays in the ellipse
        Assert.True(probe.OnTarget);
        probe.Tick(false, 0, 0);
        Assert.Equal(held, probe.Progress, 8); // one gained tick, one lost; prior dwell survives
        for (int i = 0; i < 20; i++) probe.Tick(true, 0, 180);
        Assert.True(probe.Complete);
        Assert.Equal(1, probe.Progress);
    }

    [Fact]
    public void HorizontalMissDoesNotAccumulateAndStaleGazeCannotPop()
    {
        var probe = new CalibrationBubbleProbe(0, 0);
        for (int i = 0; i < 100; i++) probe.Tick(true, 131, 0);
        Assert.Equal(0, probe.Progress);
        for (int i = 0; i < 100; i++) probe.Tick(false, 0, 0);
        Assert.False(probe.Complete);
    }

    [Fact]
    public void Median_UsesTheUpperCoordinateMedianLikeWpf()
    {
        Assert.Equal((5.0, 20.0), CalibrationBubbleProbe.Median(new[] { (1.0, 30.0), (5.0, 10.0), (9.0, 20.0), (3.0, 0.0) }));
        Assert.Equal(5, CalibrationBubbleProbe.Distance((0, 0), (3, 4)));
    }
}
