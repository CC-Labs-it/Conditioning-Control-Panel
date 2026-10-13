using System;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The bubble-field numbers WPF ChaosModeService.SpawnTick / OnBenignPopped used to
/// compute inline, plus the moved payload/variant seam. Expected values are worked by hand from
/// the original lines.</summary>
public class ChaosFieldMathTests
{
    [Theory]
    [InlineData(0.0, 1.0, 6)]     // early field
    [InlineData(1.0, 1.0, 16)]    // late field
    [InlineData(0.5, 4.0, 22)]    // 11 x sqrt(4)
    public void MaxConcurrentGrowsWithIntensityAndRootDifficulty(double intensity, double diff, int expected)
        => Assert.Equal(expected, ChaosFieldMath.MaxConcurrent(intensity, diff));

    [Theory]
    [InlineData(0.5, 2.0, 0.65)]
    [InlineData(0.95, 2.0, 1.0)]  // clamped high
    [InlineData(0.0, 0.5, 0.0)]   // clamped low
    public void EffectiveIntensityIsNudgedByDifficultyAndClamped(double intensity, double diff, double expected)
        => Assert.Equal(expected, ChaosFieldMath.EffectiveIntensity(intensity, diff), 9);

    [Theory]
    [InlineData(0.0, 1.0, 1.0, 1.0, 1.0, 1000)]          // 1000 ms early
    [InlineData(1.0, 1.0, 1.0, 1.0, 1.0, 320)]           // 320 ms late
    [InlineData(1.0, 2.0, 1.0, 1.0, 1.0, 280)]           // 160 floored at 280
    [InlineData(0.0, 1.0, 0.5, 1.0, 1.0, 2000)]          // half the spawn rate doubles the gap
    [InlineData(0.0, 1.0, 100, 1.0, 1.0, 280)]           // spawn rate clamped to 10x, then floored
    [InlineData(1.0, 1.0, 1.0, 0.12, 1.0, 320 / 0.12)]   // slow-mo stretches the cadence
    [InlineData(1.0, 2.0, 1.0, 1.0, 1.5, 420)]           // backoff applies AFTER the floor
    public void SpawnIntervalFollowsTheWpfCadence(double intensity, double diff, double rate,
                                                 double slowMo, double backoff, double expected)
        => Assert.Equal(expected, ChaosFieldMath.SpawnIntervalMs(intensity, diff, rate, slowMo, backoff), 6);

    [Fact]
    public void TreatPointsMultiplyEveryPayLayer()
        // BasePoints(50) = 120; x0.4 baseline x3 heavy x1 pendulum x2 coin x1.5 total x1 boon
        => Assert.Equal(432, ChaosFieldMath.TreatPoints(50, 0.4, 3, 1, 2, 1.5, 1), 9);

    [Fact]
    public void PrismPaysTenTimesBase()
        => Assert.Equal(3600, ChaosFieldMath.PrismPoints(50, 1.5, 2), 9);

    [Fact]
    public void HeaviesRefuelMoreFocus()
    {
        Assert.Equal(ChaosTuning.FOCUS_PER_POP, ChaosFieldMath.TreatFocusGain(1.0));
        Assert.Equal(ChaosTuning.FOCUS_PER_HEAVY, ChaosFieldMath.TreatFocusGain(3.0));
    }

    [Theory]
    [InlineData(true, 1.0, true)]
    [InlineData(false, 1.0, false)]
    [InlineData(true, 0.0, false)]
    public void BlankEyesFloatsOnlyRealPayouts(bool on, double pts, bool expected)
        => Assert.Equal(expected, ChaosFieldMath.ShowsPopScore(on, pts));

    [Fact]
    public void PayloadFireGoesThroughTheHeadHookAndSwallowsItsFailure()
    {
        var saved = EffectPayload.Firer;
        try
        {
            EffectPayload? fired = null;
            EffectPayload.Firer = p => fired = p;
            var payload = new SubliminalPayload();
            payload.Fire();
            Assert.Same(payload, fired);

            EffectPayload.Firer = _ => throw new InvalidOperationException("head effect broke");
            new FlashPayload().Fire();   // a broken effect must never escape into the pop path

            EffectPayload.Firer = null;
            new FlashPayload().Fire();   // unseeded head: firing is a no-op
        }
        finally { EffectPayload.Firer = saved; }
    }

    [Fact]
    public void VariantTintsAreOpaqueArgb()
    {
        Assert.Equal(0xFFC8A8FFu, ChaosBubbleVariants.BuildPrism(0.5).Tint);   // was Color.FromRgb(0xC8,0xA8,0xFF)
        foreach (var v in ChaosBubbleVariants.All)
            Assert.Equal(0xFFu, v.Tint >> 24);
    }
}
