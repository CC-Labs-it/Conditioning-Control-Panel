using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// SubliminalCard against WPF SubliminalService.ShowSubliminalVisuals / BuildSubliminalContent: hold is
// frames x 17 ms (min 100, override wins but keeps the floor), 50 ms fades (min 1), percent opacity,
// Bold 120 DIP with the 8 outline offsets. Moved here from the Avalonia head's TextOverlayTests.
public sealed class SubliminalCardTests
{
    [Fact]
    public void Hold_is_frames_times_17_min_100()
    {
        Assert.Equal(100, SubliminalCard.HoldMs(2));
        Assert.Equal(85 * 17, SubliminalCard.HoldMs(85));
    }

    [Fact]
    public void Hold_override_wins_but_keeps_the_floor()
    {
        Assert.Equal(2400, SubliminalCard.HoldMs(85, 2400));
        Assert.Equal(100, SubliminalCard.HoldMs(85, 30));
    }

    [Fact]
    public void Fade_defaults_to_50_and_never_drops_under_1()
    {
        Assert.Equal(50, SubliminalCard.FadeMs(null));
        Assert.Equal(400, SubliminalCard.FadeMs(400));
        Assert.Equal(1, SubliminalCard.FadeMs(0));
    }

    [Fact]
    public void Alpha_is_percent_clamped_to_0_1()
    {
        Assert.Equal(0.8, SubliminalCard.Alpha(80), 6);
        Assert.Equal(1, SubliminalCard.Alpha(150));
        Assert.Equal(0, SubliminalCard.Alpha(-5));
    }

    [Fact]
    public void Look_matches_wpf()
    {
        Assert.Equal(120, SubliminalCard.FontDip);
        Assert.Equal(new (double, double)[] { (-3, -3), (3, -3), (-3, 3), (3, 3), (0, -4), (0, 4), (-4, 0), (4, 0) },
            SubliminalCard.OutlineOffsets);
    }

    // The offsets are shared by WPF SubliminalService, SubliminalLayer and the Avalonia overlay;
    // before the move each kept a private copy, so none could change another's.
    [Fact]
    public void Outline_offsets_cannot_be_written()
    {
        var list = Assert.IsAssignableFrom<IList<(double X, double Y)>>(SubliminalCard.OutlineOffsets);
        Assert.Throws<NotSupportedException>(() => list[0] = (9, 9));
        Assert.Equal((-3.0, -3.0), SubliminalCard.OutlineOffsets[0]);
    }
}
