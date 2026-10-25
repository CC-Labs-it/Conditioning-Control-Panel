using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class WebcamMouthAndTrailTests
{
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static MouthOpenDetector Seed(double baseline = .1)
    {
        var d = new MouthOpenDetector();
        for (int i = 0; i < 20; i++) Assert.False(d.Update(baseline, Epoch.AddMilliseconds(i * 30)));
        return d;
    }

    [Fact]
    public void Geometry_UsesThreeChordsAndIgnoresDepth()
    {
        var lm = new float[468][];
        for (int i = 0; i < lm.Length; i++) lm[i] = new float[3];
        lm[308][0] = 2;
        lm[81][1] = 1; lm[13][1] = 2; lm[311][1] = 3;
        lm[402][2] = 1000;
        Assert.Equal(1, MouthOpenDetector.ComputeMar(lm), 6);
        lm[308][0] = 0;
        Assert.Equal(0, MouthOpenDetector.ComputeMar(lm));
    }

    [Theory]
    [InlineData(0, .40, false)]
    [InlineData(.5, .40, true)]
    [InlineData(1, .25, true)]
    [InlineData(.5, .30, false)]
    public void SensitivityAndSpeechFloor(double sensitivity, double mar, bool fires)
    {
        var d = Seed(); int count = 0;
        for (int i = 0; i < 40; i++) if (d.Update(mar, Epoch.AddMilliseconds(1000 + i * 30), sensitivity)) count++;
        Assert.Equal(fires ? 1 : 0, count);
        Assert.Equal(.1, d.Baseline, 6);
    }

    [Fact]
    public void MedianTimingCooldownAndFaceLoss()
    {
        var d = Seed();
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(1000)));
        Assert.False(d.IsOpen); // one-frame landmark spike
        Assert.False(d.Update(.1, Epoch.AddMilliseconds(1030)));
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(1100)));
        Assert.True(d.IsOpen);
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(1349)));
        Assert.True(d.Update(.6, Epoch.AddMilliseconds(1350)));
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(2000))); // held opening fires once
        d.FaceMissing();
        Assert.False(d.IsOpen);
        Assert.Equal(.1, d.Baseline, 6);
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(2001)));
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(2250))); // only 249ms since reacquisition
        Assert.True(d.Update(.6, Epoch.AddMilliseconds(2251)));
        d.Reset();
        Assert.Equal(0, d.ClosedSamples);
        Assert.Equal(0, d.Count);
    }

    [Fact]
    public void ReopeningWaitsForCooldownAndRestartRetainsIt()
    {
        var d = Seed();
        d.Update(.6, Epoch.AddMilliseconds(1000));
        d.Update(.6, Epoch.AddMilliseconds(1030));
        Assert.True(d.Update(.6, Epoch.AddMilliseconds(1280)));
        d.FaceMissing();
        d.Update(.6, Epoch.AddMilliseconds(1300));
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(1550)));
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(2079)));
        Assert.True(d.Update(.6, Epoch.AddMilliseconds(2080)));
        d.Reset();
        for (int i = 0; i < 20; i++) d.Update(.1, Epoch.AddMilliseconds(2090 + i));
        d.Update(.6, Epoch.AddMilliseconds(2120));
        d.Update(.6, Epoch.AddMilliseconds(2130));
        Assert.False(d.Update(.6, Epoch.AddMilliseconds(2380)));
        Assert.True(d.Update(.6, Epoch.AddMilliseconds(2880)));
    }

    [Fact]
    public void HighClosedBaselineDoesNotLatchAbsoluteHysteresis()
    {
        var d = Seed(.4);
        for (int i = 0; i < 15; i++) d.Update(.8, Epoch.AddMilliseconds(1000 + 30 * i));
        Assert.True(d.IsOpen);
        for (int i = 0; i < 3; i++) d.Update(.4, Epoch.AddMilliseconds(1500 + 30 * i));
        Assert.False(d.IsOpen);
    }

    [Fact]
    public void TrailBoundsDistanceAndClampedAge()
    {
        var t = new GazeCursorTrail();
        t.Tick(0, (0, 0)); t.Tick(0, (2.99, 0));
        Assert.Equal(1, t.Count);
        t.Tick(0, (3, 0)); Assert.Equal(2, t.Count);
        for (int i = 2; i < 100; i++) t.Tick(0, (3 * i, 0));
        Assert.Equal(64, t.Count);
        Assert.Equal(108, t[0].X);
        t.Tick(5, null); Assert.Equal(.1f, t[0].Age);
        for (int i = 0; i < 5; i++) t.Tick(.1, null);
        Assert.Equal(0, t.Count);
        t.Tick(0, (0, 0)); t.Clear(); Assert.Equal(0, t.Count);
    }
}
