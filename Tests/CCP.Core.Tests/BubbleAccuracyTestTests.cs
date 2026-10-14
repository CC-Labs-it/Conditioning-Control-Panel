using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The calibration bubble test's rules (WPF RunBubbleTestAsync, now Core BubbleAccuracyTest) on
/// synthetic gaze points.</summary>
public class BubbleAccuracyTestTests
{
    private const double RingHalfH = 114;   // TestBubbleRingBg is 228 tall in both heads

    [Theory]
    [InlineData(1920, 1080, 222, 950)]    // ideal span does not fit under the caption: compressed, recentred
    [InlineData(2560, 1440, 222, 1230)]   // fits: full 0.70 h span, centre as near h/2 as the band allows
    public void ProbeSpots_AreTwoMirrorPairs_InsideTheBand(double w, double h, double top, double bottom)
    {
        var s = BubbleAccuracyTest.ProbeSpots(w, h, RingHalfH);
        Assert.Equal(4, s.Length);
        Assert.Equal(new[] { w * 0.24, w * 0.76, w * 0.24, w * 0.76 }, s.Select(p => p.X));
        Assert.Equal(top, s[0].Y, 6);
        Assert.Equal(bottom, s[2].Y, 6);
        // Both columns share one mean height (no vertical leak into the x fit) and all four rows differ.
        Assert.Equal((s[0].Y + s[2].Y) / 2, (s[1].Y + s[3].Y) / 2, 6);
        Assert.Equal(4, s.Select(p => Math.Round(p.Y, 3)).Distinct().Count());
        Assert.True(s[3].Y < s[1].Y);   // visit order: top, lower-mid, bottom, upper-mid
    }

    [Fact]
    public void ProbeSpots_TooShortAWindow_CollapsesToOneLine()
    {
        var s = BubbleAccuracyTest.ProbeSpots(800, 300, RingHalfH);
        Assert.All(s, p => Assert.Equal(150, p.Y));
    }

    [Fact]
    public void Dwell_PopsAfter700msOnTheRollingMean()
    {
        var d = new BubbleAccuracyTest.Dwell(500, 400);
        // The first two frames only seed the 3-sample mean; 22 on-target ticks of 33 ms reach 700 ms.
        for (int i = 0; i < 23; i++) d.Tick(i * 33, (500, 400), (520, 390));
        Assert.False(d.Popped);
        d.Tick(23 * 33, (500, 400), (520, 390));
        Assert.True(d.Popped);
        Assert.Equal((520.0, 390.0), d.PopSample);   // the pre-lock point, not the locked one
    }

    [Fact]
    public void Dwell_OneWobbleFrameKeepsCounting_StaleGazeDecays()
    {
        var d = new BubbleAccuracyTest.Dwell(500, 400);
        for (int i = 0; i < 10; i++) d.Tick(i * 33, (500, 400), (500, 400));
        double held = d.DwellMs;
        d.Tick(330, (1500, 400), (1500, 400));   // one frame far outside: the mean is still on it
        Assert.Equal(held + 33, d.DwellMs);
        d.Tick(363, null, default);                // no fresh gaze: leaky decay, not a reset
        Assert.Equal(held, d.DwellMs);
    }

    [Fact]
    public void NearMiss_IsAnEllipticalGate_AndNeeds10Samples()
    {
        var wide = new BubbleAccuracyTest.Dwell(500, 400);
        var tall = new BubbleAccuracyTest.Dwell(500, 400);
        var outside = new BubbleAccuracyTest.Dwell(500, 400);
        for (int i = 0; i < 9; i++) wide.Tick(i * 33, (700, 400), (700, 400));
        Assert.Null(wide.NearMissSample());
        wide.Tick(9 * 33, (700, 400), (700, 400));
        Assert.Equal((700.0, 400.0), wide.NearMissSample());
        for (int i = 0; i < 10; i++) tall.Tick(i * 33, (500, 700), (500, 700));   // 300 down: a real vertical miss
        Assert.Equal((500.0, 700.0), tall.NearMissSample());
        for (int i = 0; i < 10; i++) outside.Tick(i * 33, (700, 700), (700, 700));
        Assert.Null(outside.NearMissSample());
    }

    private static ((double X, double Y) Target, (double X, double Y) Measured)[] Pairs(Func<(double X, double Y), (double X, double Y)> measure) =>
        BubbleAccuracyTest.ProbeSpots(1920, 1080, RingHalfH).Select(t => (t, measure(t))).ToArray();

    [Fact]
    public void FitTrim_RecoversAConstantOffset()
    {
        var fit = BubbleAccuracyTest.FitTrim(Pairs(t => (t.X + 40, t.Y - 30)), 1920, 1080)!;
        Assert.Equal(-40, fit.X0, 6);
        Assert.Equal(30, fit.Y0, 6);
        Assert.Equal(0, fit.X1, 6);
        Assert.Equal(50, fit.RmsBefore, 6);
        Assert.Equal(0, fit.RmsAfter, 6);
        Assert.True(fit.Apply);
        Assert.Equal("Popped 4 / 4 · average accuracy 50 px · recalibrated from your aim (50→0 px). Run it again to verify!",
            BubbleAccuracyTest.Summary(4, 4, new[] { 50.0, 50, 50, 50 }, fit));
    }

    [Fact]
    public void FitTrim_FitsAShrunkMap_AtHalfStrength()
    {
        // The cursor covers only 90% of the screen about its centre: residual = u / 9, damped to u / 18.
        var fit = BubbleAccuracyTest.FitTrim(Pairs(t => (960 + (t.X - 960) * 0.9, t.Y)), 1920, 1080)!;
        Assert.Equal(1.0 / 18, fit.X1, 6);
        Assert.Equal(0, fit.X0, 6);
        Assert.True(fit.RmsAfter < fit.RmsBefore);
    }

    [Fact]
    public void FitTrim_NoiseLevelOrTooFewSamples_DoesNotApply()
    {
        var fit = BubbleAccuracyTest.FitTrim(Pairs(t => (t.X + 1, t.Y + 1)), 1920, 1080)!;
        Assert.False(fit.Apply);
        Assert.Equal("Popped 2 / 4 · average accuracy 2 px.", BubbleAccuracyTest.Summary(2, 4, new[] { 1.0, 3 }, fit));
        Assert.Null(BubbleAccuracyTest.FitTrim(Pairs(t => t).Take(1).ToArray(), 1920, 1080));
        Assert.Equal("Couldn't pop any bubbles — tracking may be off for this setup. Consider recalibrating.",
            BubbleAccuracyTest.Summary(0, 4, Array.Empty<double>(), null));
    }

    [Fact]
    public void GazeTrim_ComposesOverTheOldTrim_AndClamps()
    {
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var c = GazeTrimData.Compose(new GazeTrimData { X0 = 10, X1 = 0.1 }, 20, 0.2, 5, 0, 960, 540, at);
        Assert.Equal(32, c.X0, 9);       // a0 + b0 + b1·a0
        Assert.Equal(0.32, c.X1, 9);     // a1 + b1 + a1·b1
        Assert.Equal(5, c.Y0, 9);
        Assert.Equal((960.0, 540.0, at), (c.CenterX, c.CenterY, c.CapturedAt));
        var wild = GazeTrimData.Compose(new GazeTrimData { X0 = 290, X1 = 0.3 }, 20, 0.2, -400, -0.5, 0, 0, at);
        Assert.Equal((300.0, 0.35, -300.0, -0.35), (wild.X0, wild.X1, wild.Y0, wild.Y1));
    }
}
