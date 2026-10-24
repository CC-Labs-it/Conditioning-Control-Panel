using System;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>E-Stim tether + halo maths (WPF ConditioningControlPanel/Chaos/ChaosEStimOverlay.cs).</summary>
public class ChaosEStimMathTests
{
    [Fact]
    public void Bolt_runs_end_to_end_with_wpf_midpoint_count_and_bounded_sideways_jitter()
    {
        // 330 DIPs along X: 330/55 = 6 midpoints; rng 1.0-ish pushes each the full +JITTER sideways (+Y).
        var p = ChaosEStimMath.BoltPath(10, 20, 340, 20, () => 0.999999);
        Assert.Equal(8, p.Length);
        Assert.Equal((10.0, 20.0), p[0]);
        Assert.Equal((340.0, 20.0), p[^1]);
        for (int m = 1; m <= 6; m++)
        {
            Assert.Equal(10 + 330 * m / 7.0, p[m].X, 6);
            Assert.Equal(20 + ChaosEStimMath.Jitter, p[m].Y, 3);
        }
        // Centred rng: no sideways offset; a short bolt still gets 2 midpoints.
        var straight = ChaosEStimMath.BoltPath(0, 0, 0, 40, () => 0.5);
        Assert.Equal(4, straight.Length);
        Assert.All(straight, q => Assert.Equal(0, q.X, 9));
        // Degenerate bolt (same point) never divides by zero.
        Assert.All(ChaosEStimMath.BoltPath(5, 5, 5, 5, () => 0.9), q => Assert.Equal((5.0, 5.0), q));
    }

    [Fact]
    public void Strike_is_hot_then_fades_linearly_then_ends()
    {
        Assert.Equal(0.85, ChaosEStimMath.StrikeOpacityAt(0));
        Assert.Equal(0.85, ChaosEStimMath.StrikeOpacityAt(119));
        Assert.Equal(0.425, ChaosEStimMath.StrikeOpacityAt(160)!.Value, 6);
        Assert.Null(ChaosEStimMath.StrikeOpacityAt(200));
        // Flicker re-jitters at 0/40/80 and freezes once the hot window is reached.
        Assert.Equal(0, ChaosEStimMath.FlickerStep(0));
        Assert.Equal(1, ChaosEStimMath.FlickerStep(40));
        Assert.Equal(2, ChaosEStimMath.FlickerStep(119));
        Assert.Equal(-1, ChaosEStimMath.FlickerStep(120));
    }

    [Fact]
    public void Halo_centres_on_the_cursor_in_physical_pixels_and_pulses_between_wpf_bounds()
    {
        Assert.Equal((454, 254), ChaosEStimMath.HaloOrigin(500, 300, 1.0));     // 92/2 = 46
        Assert.Equal((431, 231), ChaosEStimMath.HaloOrigin(500, 300, 1.5));     // 92*1.5/2 = 69
        Assert.Equal((100.0, 50.0), ChaosEStimMath.ToLocal(1400, 300, 1200, 200, 2.0));
        Assert.Equal(0.88, ChaosEStimMath.HaloScaleAt(0), 6);
        Assert.Equal(1.10, ChaosEStimMath.HaloScaleAt(160), 6);
        Assert.Equal(0.99, ChaosEStimMath.HaloScaleAt(80), 6);
        Assert.Equal(0.88, ChaosEStimMath.HaloScaleAt(320), 6);
    }
}
