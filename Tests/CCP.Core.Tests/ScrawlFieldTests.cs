using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Scrawl's shared state (moved from the WPF ScrawlScene for the Linux head): no head, no font.</summary>
public class ScrawlFieldTests
{
    private static readonly ScrawlBounds Screen = new(0, 0, 1920, 1080);

    private static ScrawlField Field() => new(72, (w, s) => (w.Length * s * 0.6, s * 1.2), () => "CORNER", new Random(3));

    [Fact]
    public void A_wall_hit_stamps_glows_and_sparks_then_everything_fades()
    {
        var f = Field();
        f.WallHit("SINK", ScrawlWall.Top, 800, 0, 1000, 80, Screen, 960, 540);
        var s = Assert.Single(f.Stamps);
        Assert.Equal(72 * ScrawlRules.StampScale, s.Size, 6);
        Assert.Equal(ScrawlRules.InkDots, s.Dots.Length);
        Assert.Single(f.Glows);
        Assert.True(f.LiveSparks > 0);
        Assert.Null(f.LabelText);
        var boxes = new List<(double X, double Y, double W, double H)>();
        f.AppendRects(boxes);
        Assert.Single(boxes);
        for (int i = 0; i < 150; i++) f.Tick(0.1, MotionLevel.Full);
        Assert.False(f.IsLive);
    }

    [Fact]
    public void A_corner_gets_the_gold_stamp_and_the_word_only_while_corner_flash_is_on()
    {
        var f = Field();
        f.Configure(14, 44, 1, cornerLabel: false);
        f.WallHit("SINK", ScrawlWall.Left, 0, 0, 200, 80, Screen, 960, 540);
        Assert.Contains(f.Stamps, s => s.Gold && s.Ink == ScrawlField.Gold);
        Assert.Null(f.LabelText);
        f.Configure(14, 44, 1, cornerLabel: true);
        int serial = f.LabelSerial;
        f.WallHit("SINK", ScrawlWall.Left, 0, 0, 200, 80, Screen, 960, 540);
        Assert.Equal("CORNER", f.LabelText);
        Assert.Equal((960.0, 540.0), (f.LabelX, f.LabelY));
        Assert.Equal(serial + 1, f.LabelSerial);
        Assert.True(f.LabelSize <= ScrawlRules.CornerLabelMaxDip);
    }

    [Fact]
    public void The_cap_drops_the_oldest_wall_stamp_and_never_the_slam()
    {
        var f = Field();
        f.SlamLanded("DROP", 960, 540, 1920);
        for (int i = 0; i < 10; i++) f.WallHit("W" + i, ScrawlWall.Top, 300 + i * 100, 0, 380 + i * 100, 80, Screen, 960, 540);
        f.Configure(14, 4, 1);
        Assert.Equal(4, f.Stamps.Count);
        Assert.Contains(f.Stamps, s => s.Slam);
        Assert.Equal(new[] { "W7", "W8", "W9" }, f.Stamps.Where(s => !s.Slam).Select(s => s.Word));
    }

    [Fact]
    public void Photosafe_and_motion_off_never_shake_and_clear_stops_at_once()
    {
        var f = Field();
        f.SlamLanded("DROP", 960, 540, 1920);
        f.Tick(0.016, MotionLevel.Full, photosafe: true);
        Assert.Equal((0.0, 0.0), (f.ShakeX, f.ShakeY));
        f.SlamLanded("DROP", 960, 540, 1920);
        f.Tick(0.016, MotionLevel.Off);
        Assert.Equal((0.0, 0.0), (f.ShakeX, f.ShakeY));
        f.Clear();
        Assert.False(f.IsLive);
        Assert.Empty(f.Stamps);
    }
}
