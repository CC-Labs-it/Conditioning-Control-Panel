using System.Drawing;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Afterglow line's host rules, shared by the WPF layer and the Avalonia overlay.</summary>
public sealed class AfterglowLineRulesTests
{
    [Fact]
    public void The_box_is_read_into_the_line_clamped_and_a_ghost_change_repaints()
    {
        var line = new AfterglowLine();
        var s = new AppSettings { AfterglowLineWords = 99, AfterglowLineHold = 0.01, AfterglowGhostOpacity = 42 };
        Assert.False(AfterglowLineRules.Apply(line, s));   // 42 percent is the line as built
        Assert.Equal(AfterglowLineOptions.WordsMax, line.Words);
        Assert.Equal(AfterglowLineOptions.HoldMin, line.HoldSeconds, 6);
        s.AfterglowGhostOpacity = 80;
        Assert.True(AfterglowLineRules.Apply(line, s));
        Assert.Equal(0.8, line.Ghost, 6);
        Assert.False(AfterglowLineRules.Apply(line, s));
        Assert.False(AfterglowLineRules.Apply(line, null));
    }

    [Fact]
    public void The_line_and_the_fog_band_sit_inside_their_monitor()
    {
        var mon = new Rectangle(1920, 0, 2560, 1440);
        var band = AfterglowLineRules.LineBand(mon);
        Assert.True(band.Left >= mon.Left && band.Right <= mon.Right && band.Bottom <= mon.Bottom);
        Assert.True(band.Top > mon.Height / 2);
        var fog = AfterglowLineRules.FogBand(mon);
        Assert.True(fog.Left > mon.Left && fog.Right < mon.Right);
        Assert.True(fog.Top < mon.Height / 2.0 && fog.Bottom > mon.Height / 2.0, "the fog writes across the middle");
    }

    [Fact]
    public void Without_a_pool_a_ghost_rises_a_little_above_the_middle_of_its_monitor()
    {
        var stage = new SuperStage();
        var (x, y) = AfterglowLineRules.WordsFrom(stage, 1920, 0, 2560, 1440);
        Assert.Equal(1920 + 1280, x, 6);
        Assert.Equal(1440 * 0.46, y, 6);
    }
}
