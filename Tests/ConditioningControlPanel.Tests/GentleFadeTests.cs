using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Gentle visuals maths for the overlay layers (layer fade, eased pulse, pop fade).</summary>
public class GentleFadeTests
{
    [Fact]
    public void StepRisesAndFallsLinearlyWithoutOvershoot()
    {
        Assert.Equal(0.5, GentleFade.Step(0, 1, 0.375, 0.75), 6);
        Assert.Equal(1.0, GentleFade.Step(0.9, 1, 0.5, 0.75));
        Assert.Equal(0.5, GentleFade.Step(1, 0, 0.375, 0.75), 6);
        Assert.Equal(0.0, GentleFade.Step(0.1, 0, 0.5, 0.75));
    }

    [Fact]
    public void StepWithNoFadeSnaps()
    {
        Assert.Equal(1.0, GentleFade.Step(0, 1, 0.016, 0));
        Assert.Equal(0.0, GentleFade.Step(1, 0, 0.016, double.NaN));
    }

    [Fact]
    public void WholeFadeTakesTheFadeLength()
    {
        double a = 0; int frames = 0;
        while (a < 1 && frames < 1000) { a = GentleFade.Step(a, 1, 1 / 60.0, 0.75); frames++; }
        Assert.InRange(frames, 44, 46);   // 0.75 s at 60 fps
    }

    [Fact]
    public void PulseEasesUpHoldsThenEasesDown()
    {
        const double rise = 750, hold = 1000, fall = 750;
        Assert.Equal(0, GentleFade.PulseEnvelope(0, rise, hold, fall));
        Assert.InRange(GentleFade.PulseEnvelope(50, rise, hold, fall), 0.0, 0.05);   // no instant jump
        Assert.Equal(0.5, GentleFade.PulseEnvelope(375, rise, hold, fall), 6);
        Assert.Equal(1, GentleFade.PulseEnvelope(900, rise, hold, fall));
        Assert.Equal(0.5, GentleFade.PulseEnvelope(1375, rise, hold, fall), 6);
        Assert.Equal(0, GentleFade.PulseEnvelope(1750, rise, hold, fall));
        Assert.Equal(1750, GentleFade.PulseTotalMs(rise, hold, fall));
    }

    [Fact]
    public void PulseRiseLongerThanTheHoldFallsRightAfterThePeak()
    {
        Assert.Equal(1, GentleFade.PulseEnvelope(2000, 2000, 1000, 2000));
        Assert.Equal(4000, GentleFade.PulseTotalMs(2000, 1000, 2000));
        Assert.True(GentleFade.PulseEnvelope(2500, 2000, 1000, 2000) < 1);
    }

    [Fact]
    public void GentlePopIsAPlainFade()
    {
        var s = FlashExit.Gentle(0.75, seed: 3);
        Assert.Equal(FlashExitStyle.Mix, s.Style);
        Assert.Equal(0, FlashExit.SparkCount(s));
        foreach (var p in new[] { 0.0, 0.3, 0.6, 0.9 })
        {
            s.ElapsedSec = 0.75 * p;
            var sample = FlashExit.Sample(s);
            Assert.Equal(1, sample.ScaleX);
            Assert.Equal(1, sample.ScaleY);
            Assert.Equal(0, sample.RotationDeg);
            Assert.Equal(0, sample.Whiten);
            Assert.Equal(0, sample.Glitch);
            Assert.Equal(1 - p, sample.Alpha, 6);
        }
    }
}
