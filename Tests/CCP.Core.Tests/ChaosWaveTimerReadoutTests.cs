using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Pocket Watch readout rules, WPF ChaosWaveTimerOverlay.SetText (:161-199).</summary>
public class ChaosWaveTimerReadoutTests
{
    [Theory]
    [InlineData(1, 5, 61.0, "WAVE 1/5", "1:01")]
    [InlineData(4, 5, 0.2, "WAVE 4/5", "0:01")]     // ceiling: 0.2 s left still shows a second
    [InlineData(5, 5, 125.0, "LAST WAVE", "2:05")]
    [InlineData(6, 5, -3.0, "LAST WAVE", "0:00")]   // never negative; past the last wave is still LAST
    public void WaveAndClockText(int wave, int count, double secLeft, string waveText, string clock)
    {
        var r = ChaosWaveTimerReadout.For(wave, count, secLeft, 0);
        Assert.Equal(waveText, r.Wave);
        Assert.Equal(clock, r.Clock);
    }

    [Fact]
    public void ScoreIsTruncatedAndGrouped()
    {
        Assert.Equal(1234.ToString("N0"), ChaosWaveTimerReadout.For(1, 5, 30, 1234.9).Score);
        Assert.Equal("0", ChaosWaveTimerReadout.For(1, 5, 30, 0.7).Score);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(210, 0.625)]
    [InlineData(420, 0.25)]     // the trough, then back up (AutoReverse)
    [InlineData(630, 0.625)]
    [InlineData(840, 1.0)]      // one full cycle, repeating
    [InlineData(1260, 0.25)]
    public void FinalRushBlinkFollowsWpfAnimation(double ms, double opacity) =>
        Assert.Equal(opacity, ChaosWaveTimerReadout.BlinkOpacity(System.TimeSpan.FromMilliseconds(ms)), 6);

    [Theory]
    [InlineData(1, 5, 10.0, true, false)]    // red from exactly ten seconds left
    [InlineData(1, 5, 10.01, false, false)]
    [InlineData(5, 5, 10.0, true, true)]     // urgent on the last wave = the blinking finale
    [InlineData(5, 5, 30.0, false, false)]
    public void WarningStates(int wave, int count, double secLeft, bool urgent, bool finalRush)
    {
        var r = ChaosWaveTimerReadout.For(wave, count, secLeft, 0);
        Assert.Equal(urgent, r.Urgent);
        Assert.Equal(finalRush, r.FinalRush);
    }
}
