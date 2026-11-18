using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The 7.3.2 tier-2 round (2026-11-18): Scrawl at big text sizes, the Stamp size and Stay centred
/// options, and the camera staying off after a stop by hand.
/// </summary>
public class TesterRound733Tests
{
    private static readonly ScrawlBounds Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void The_stamp_cap_is_untouched_at_stock_text_size()
        => Assert.Equal(44, ScrawlOptions.EffectiveCap(44, 100, 1));

    [Theory]
    [InlineData(44, 300, 1, 15)]   // the report: 300 percent text held 44 giant stamps
    [InlineData(44, 300, 2, 7)]    // and Second text doubled the hits
    [InlineData(44, 50, 1, 44)]    // small stamps never raise the cap past the slider
    [InlineData(6, 300, 2, 4)]     // the floor
    [InlineData(80, 100, 2, 40)]
    public void The_stamp_cap_shrinks_with_stamp_size_and_a_second_word(int cap, double percent, int words, int expected)
        => Assert.Equal(expected, ScrawlOptions.EffectiveCap(cap, percent, words));

    [Theory]
    [InlineData(100, 100)]
    [InlineData(10, 25)]
    [InlineData(400, 150)]
    [InlineData(62, 60)]
    [InlineData(63, 65)]
    public void Stamp_size_clamps_and_snaps_to_five(int raw, int expected)
        => Assert.Equal(expected, ScrawlOptions.ClampStampSize(raw));

    [Fact]
    public void Stamp_size_defaults_to_the_text_size_and_clamps_on_the_setting()
    {
        Assert.Equal(100, new AppSettings().ScrawlStampSize);
        Assert.Equal(150, new AppSettings { ScrawlStampSize = 900 }.ScrawlStampSize);
    }

    [Fact]
    public void The_corner_zone_stops_growing_with_the_text()
    {
        // A word 200 DIP from the top wall bouncing off the left wall. At 300 percent text (216 DIP)
        // the old zone was 302 DIP deep, so this side bounce counted as a corner.
        Assert.False(ScrawlRules.IsCorner(0, 200, 1100, 416, Screen, 216));
        // A real corner still counts at any size.
        Assert.True(ScrawlRules.IsCorner(0, 40, 1100, 256, Screen, 216));
        Assert.True(ScrawlRules.IsCorner(0, 90, 300, 170, Screen, 72));
    }

    [Fact]
    public void Stay_centred_parks_the_pool_in_the_middle_and_counts_the_mouse_as_resting()
    {
        var off = WhirlpoolOptions.Anchor(false, 100, 200, 900, 960, 540);
        Assert.Equal((100d, 200d, 900d), off);
        var on = WhirlpoolOptions.Anchor(true, 100, 200, 900, 960, 540);
        Assert.Equal((960d, 540d, 0d), on);
    }

    [Fact]
    public void Stay_centred_is_off_by_default()
        => Assert.False(new AppSettings().WhirlpoolStayCentred);

    [Fact]
    public void The_camera_is_not_held_off_until_the_player_stops_it()
        => Assert.False(new AppSettings().CameraHeldOff);

    [Fact]
    public void The_afterglow_line_band_sits_at_the_bottom_of_its_monitor()
    {
        var band = ConditioningControlPanel.Services.Compositor.AfterglowLineLayer.LineBand(
            new System.Drawing.Rectangle(1920, 0, 2560, 1440));
        Assert.True(band.Left >= 1920 && band.Right <= 1920 + 2560);
        Assert.True(band.Top > 720, "never the middle of the screen");
        Assert.True(band.Bottom <= 1440);
        // The baseline (11 percent from the bottom) is inside it.
        Assert.InRange(1440 - (int)(1440 * 0.11), band.Top, band.Bottom);
    }
}
