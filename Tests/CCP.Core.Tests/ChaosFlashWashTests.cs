using System.Linq;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The braindrain wash envelope, against WPF ConditioningControlPanel/Chaos/ChaosFlashOverlay.cs:
/// 10 s / 10% default, peak clamped 0.02..1, 500 ms linear fade-in, life max(600, duration), 700 ms fade-out.</summary>
public sealed class ChaosFlashWashTests
{
    [Fact]
    public void DefaultsAreWpfs()
    {
        Assert.Equal(10000, ChaosFlashWash.DefaultDurationMs);
        Assert.Equal(0.10, ChaosFlashWash.DefaultOpacity);
        Assert.Equal(1280, ChaosFlashWash.MaxDecodeDim);
        Assert.Equal(40, ChaosFlashWash.MaxFrames);
    }

    [Theory]
    [InlineData(4500, 0.30, 0.30, 4500)]   // glitch bubble: ~30%
    [InlineData(100, 0.001, 0.02, 600)]    // floor on both
    [InlineData(10000, 5.0, 1.0, 10000)]
    [InlineData(10000, double.NaN, 0.02, 10000)]
    public void PlanClampsPeakAndLife(int duration, double opacity, double peak, int fadeOutAt)
    {
        var p = ChaosFlashWash.For(duration, opacity);
        Assert.Equal(peak, p.Peak, 10);
        Assert.Equal(fadeOutAt, p.FadeOutAtMs);
        Assert.Equal(fadeOutAt + 700, p.EndMs);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(250, 0.05)]      // half way up the 500 ms fade-in
    [InlineData(500, 0.10)]
    [InlineData(9999, 0.10)]     // holding
    [InlineData(10000, 0.10)]    // fade-out begins
    [InlineData(10350, 0.05)]    // half way down the 700 ms fade-out
    [InlineData(10700, 0.0)]
    [InlineData(20000, 0.0)]
    public void AlphaFollowsTheWpfCurve(double ms, double alpha)
        => Assert.Equal(alpha, ChaosFlashWash.AlphaAt(ChaosFlashWash.For(10000, 0.10), ms), 10);

    [Fact]
    public void KeyframesRampUpHoldThenEndAtZero()
    {
        var p = ChaosFlashWash.For(10000, 0.10);
        var k = ChaosFlashWash.Keyframes(p, 4);
        Assert.Equal(new[] { 125, 250, 375, 500, 10175, 10350, 10525, 10700 }, k.Select(x => x.AtMs));
        Assert.Equal(0.10, k[3].Alpha, 10);
        Assert.Equal(0.05, k[5].Alpha, 10);
        Assert.Equal((10700, 0.0), k[^1]);
    }
}
