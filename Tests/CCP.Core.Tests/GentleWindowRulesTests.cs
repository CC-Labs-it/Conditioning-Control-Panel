using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Gentle visuals, "windows" lane (video windows, lock card, Chaos overlays): the pure rules.
/// Off must be today's behaviour, so every rule is pinned at its off value too.
/// </summary>
public class GentleWindowRulesTests
{
    [Fact]
    public void PlaybackRate_OffIsAlwaysOne()
    {
        Assert.Equal(1f, GentleWindowRules.PlaybackRate(on: false, speed: 0.25));
        Assert.Equal(1f, GentleWindowRules.PlaybackRate(on: false, speed: 0.5));
    }

    [Theory]
    [InlineData(0.5, 0.5f)]
    [InlineData(0.1, 0.25f)]   // never slower than the floor
    [InlineData(3.0, 1f)]      // never faster than normal
    public void PlaybackRate_OnFollowsTheClampedSpeed(double speed, float expected)
        => Assert.Equal(expected, GentleWindowRules.PlaybackRate(on: true, speed: speed));

    [Theory]
    [InlineData(60.0, 1.0, 60.0)]    // normal speed: the guillotine is unchanged
    [InlineData(60.0, 0.5, 120.0)]   // half speed takes twice the wall time
    [InlineData(60.0, 0.25, 240.0)]
    [InlineData(60.0, 0.0, 60.0)]    // junk rates never shrink or blow up the timer
    [InlineData(60.0, double.NaN, 60.0)]
    public void WallSeconds_StretchesWithASlowedClip(double clip, double rate, double expected)
        => Assert.Equal(expected, GentleWindowRules.WallSeconds(clip, rate), 6);

    [Fact]
    public void SoftExit_OnlyANaturalEndWithGentleOn()
    {
        Assert.True(GentleWindowRules.SoftExit(on: true, naturalEnd: true));
        Assert.False(GentleWindowRules.SoftExit(on: true, naturalEnd: false));   // panic / Esc / forced stay instant
        Assert.False(GentleWindowRules.SoftExit(on: false, naturalEnd: true));   // off = today
    }

    [Fact]
    public void RiseHoldFade_StartsDarkRisesHoldsAndEndsDark()
    {
        var frames = GentleWindowRules.RiseHoldFade(0.85, riseMs: 300, holdMs: 120, fadeMs: 750);
        Assert.Equal(new[] { (0.0, 0), (0.85, 300), (0.85, 420), (0.0, 1170) }, frames);
    }

    [Fact]
    public void RiseHoldFade_NegativeHoldCollapsesToNoHold()
    {
        // A field zone whose life is shorter than rise + fade just rises and fades.
        var frames = GentleWindowRules.RiseHoldFade(0.8, riseMs: 300, holdMs: -200, fadeMs: 750);
        Assert.Equal(300, frames[2].Ms);
        Assert.Equal(1050, frames[3].Ms);
    }
}
