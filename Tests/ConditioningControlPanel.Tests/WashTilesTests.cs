using System.Windows;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The full-screen picture wash is sized from each monitor, never from the whole desktop.</summary>
public class WashTilesTests
{
    [Fact]
    public void One_screen_is_one_tile_the_size_of_the_stage()
    {
        var stage = new Rect(0, 0, 2560, 1440);
        var tiles = WashTiles.For(stage, new[] { stage }, 1);
        Assert.Equal(new[] { new Rect(0, 0, 2560, 1440) }, tiles);

        // Scaled display: the same single tile, in the window's DIPs.
        Assert.Equal(new[] { new Rect(0, 0, 1280, 720) }, WashTiles.For(stage, new[] { stage }, 2));
    }

    [Fact]
    public void Three_screens_side_by_side_get_a_tile_each_limited_by_their_own_height()
    {
        // The left screen sits at a negative x, as it does when the middle one is the primary.
        var screens = new[] { new Rect(1920, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1080), new Rect(0, 0, 1920, 1080) };
        var tiles = WashTiles.For(new Rect(-1920, 0, 5760, 1080), screens, 1);
        Assert.Equal(new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080), new Rect(3840, 0, 1920, 1080) }, tiles);
        foreach (var t in tiles) Assert.True(t.Width <= 1920 && t.Height <= 1080);
    }

    [Fact]
    public void Uneven_screens_keep_their_own_boxes_and_stay_inside_the_stage()
    {
        var stage = new Rect(0, 0, 1920 + 1080, 1920);
        var tiles = WashTiles.For(stage, new[] { new Rect(0, 420, 1920, 1080), new Rect(1920, 0, 1080, 1920) }, 1.5);
        Assert.Equal(new[] { new Rect(0, 280, 1280, 720), new Rect(1280, 0, 720, 1280) }, tiles);
    }

    [Fact]
    public void Mirrored_outside_and_missing_screens_never_leave_the_stage_bare()
    {
        var stage = new Rect(0, 0, 1920, 1080);
        // A mirror counts once, a screen outside the stage is dropped, one half outside is cut.
        var tiles = WashTiles.For(stage, new[] { stage, stage, new Rect(5000, 0, 1920, 1080), new Rect(960, 0, 1920, 1080) }, 1);
        Assert.Equal(new[] { new Rect(0, 0, 1920, 1080), new Rect(960, 0, 960, 1080) }, tiles);

        Assert.Equal(new[] { new Rect(0, 0, 1920, 1080) }, WashTiles.For(stage, new Rect[0], 1));
        Assert.Equal(new[] { new Rect(0, 0, 1920, 1080) }, WashTiles.For(stage, new[] { stage }, 0));   // a bad scale reads as 1
    }
}
