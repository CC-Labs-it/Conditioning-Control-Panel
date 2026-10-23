using System;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Rabbit Caller's cursor halo maths against the WPF overlay's numbers.</summary>
public class ChaosCursorGlowMathTests
{
    [Theory]
    [InlineData(500, 300, 1.0, 462, 262)]     // 76/2 = 38 px at 100 %
    [InlineData(500, 300, 2.0, 424, 224)]     // 76 px at 200 %
    [InlineData(500, 300, 1.25, 452, 252)]    // 47.5 px; 452.5 rounds to even
    [InlineData(0, 0, 0.0, -38, -38)]         // bad scaling falls back to 1
    public void Halo_centre_sits_on_the_cursor(int cx, int cy, double scaling, int x, int y)
    {
        var p = ChaosCursorGlowMath.TopLeftPx(cx, cy, scaling);
        Assert.Equal((x, y), p);
    }

    [Fact]
    public void Parked_off_screen_two_sizes_up_left()
    {
        Assert.Equal((-152, -152), ChaosCursorGlowMath.ParkedPx(1));
        Assert.Equal((-304, -304), ChaosCursorGlowMath.ParkedPx(2));
    }

    [Fact]
    public void Breath_goes_085_to_112_and_back_every_620ms_with_sine_ease()
    {
        double At(double ms) => ChaosCursorGlowMath.PulseScale(TimeSpan.FromMilliseconds(ms));
        Assert.Equal(0.85, At(0), 6);
        Assert.Equal(0.985, At(310), 6);          // eased midpoint
        Assert.Equal(1.12, At(620), 6);
        Assert.Equal(0.985, At(930), 6);          // on the way back
        Assert.Equal(0.85, At(1240), 6);
        Assert.Equal(At(155), At(1240 + 155), 6); // repeats forever
        // Ease-in: a quarter of the way in, the scale has moved less than a quarter of the range.
        Assert.True(At(155) - 0.85 < 0.27 / 4);
        Assert.Equal(0.85, At(-50), 6);
    }

    [Fact]
    public void Timing_matches_wpf()
    {
        Assert.Equal(76, ChaosCursorGlowMath.Size);
        Assert.Equal(TimeSpan.FromMilliseconds(16), ChaosCursorGlowMath.FollowInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(620), ChaosCursorGlowMath.PulseHalfPeriod);
    }
}
