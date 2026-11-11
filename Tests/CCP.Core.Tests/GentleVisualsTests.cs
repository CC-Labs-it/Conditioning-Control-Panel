using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Gentle visuals (photosensitivity switch, 2026-10-14): the pure rules every surface reads.
/// Each surface's wiring is covered next to that surface; these pin the shared arithmetic.
/// </summary>
public class GentleVisualsTests
{
    [Theory]
    [InlineData(0.0, 0.75)]   // an instant appear becomes the gentle fade
    [InlineData(0.4, 0.75)]
    [InlineData(1.5, 1.5)]    // a surface that already fades slower keeps its own length
    public void Fade_OnlyEverLengthens(double normal, double expected)
        => Assert.Equal(expected, GentleVisuals.Fade(normal, on: true, gentleSeconds: 0.75), 6);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.4)]
    public void Fade_OffLeavesTheSurfaceAlone(double normal)
        => Assert.Equal(normal, GentleVisuals.Fade(normal, on: false, gentleSeconds: 2.0), 6);

    [Fact]
    public void Fade_ClampsAnOutOfRangeChoice()
    {
        Assert.Equal(GentleVisuals.MaxFadeSeconds, GentleVisuals.Fade(0, true, 99), 6);
        Assert.Equal(GentleVisuals.MinFadeSeconds, GentleVisuals.Fade(0, true, 0.01), 6);
        Assert.Equal(0.0, GentleVisuals.Fade(double.NaN, false, 1), 6);
    }

    [Theory]
    [InlineData(100, 0.5, 200)]
    [InlineData(100, 0.25, 400)]
    [InlineData(100, 1.0, 100)]
    [InlineData(100, 4.0, 100)]   // speed never goes above 1: gentle can only slow things down
    [InlineData(100, 0.0, 400)]   // nor below the floor
    public void ScaleDelay_SlowsByTheChosenSpeed(double delay, double speed, double expected)
        => Assert.Equal(expected, GentleVisuals.ScaleDelayMs(delay, speed), 6);

    [Theory]
    [InlineData(40, true, GentleVisuals.RiseFloorMs)]
    [InlineData(500, true, 500)]
    [InlineData(40, false, 40)]
    public void Rise_HasAFloorWhileOn(int normal, bool on, int expected)
        => Assert.Equal(expected, GentleVisuals.RiseMs(normal, on));

    [Fact]
    public void Settings_ClampAndDefault()
    {
        var s = new AppSettings();
        Assert.False(s.GentleVisuals);
        Assert.Equal(GentleVisuals.DefaultFadeSeconds, s.GentleFadeSeconds, 6);
        Assert.Equal(GentleVisuals.DefaultSpeed, s.GentleSpeed, 6);
        s.GentleFadeSeconds = 10; s.GentleSpeed = 3;
        Assert.Equal(GentleVisuals.MaxFadeSeconds, s.GentleFadeSeconds, 6);
        Assert.Equal(GentleVisuals.MaxSpeed, s.GentleSpeed, 6);
    }

    [Fact]
    public void Choices_StayInsideTheClamp()
    {
        foreach (var f in GentleVisuals.FadeChoices) Assert.Equal(f, GentleVisuals.ClampFade(f), 6);
        foreach (var v in GentleVisuals.SpeedChoices) Assert.Equal(v, GentleVisuals.ClampSpeed(v), 6);
    }
}
