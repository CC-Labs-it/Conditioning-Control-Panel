using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Lights Down frame step both heads share (moved out of WPF LightsDownService for the Linux head).</summary>
public sealed class LightsDownRunTests
{
    private static readonly List<LightsDownRect> NoCut = new();

    [Fact]
    public void A_screen_maps_desktop_pixels_and_dips_into_its_own_pixels()
    {
        var s = new LightsDownScreen(1920, 0, 2560, 1440, 1.25);
        Assert.Equal(new LightsDownRect(80, 10, 100, 50), s.ToLocal(new LightsDownRect(2000, 10, 100, 50)));
        Assert.Equal(new LightsDownRect(1600 * 1.25 - 1920, 8 * 1.25, 125, 62.5), s.DipToLocal(new LightsDownRect(1600, 8, 100, 50)));
        Assert.True(s.ContainsPx(1920, 0));
        Assert.False(s.ContainsPx(1919, 0));
        Assert.True(s.Touches(new LightsDownRect(1800, 0, 100, 100), s.HaloMarginPx));
        Assert.False(s.Touches(new LightsDownRect(1000, 0, 100, 100), s.HaloMarginPx));
    }

    [Fact]
    public void The_camera_counts_only_while_it_already_runs_and_the_gaze_is_fresh()
    {
        Assert.Equal(LightsDownEyes.None, LightsDownEyes.From(false, true, true, 0, 1, 1));
        Assert.True(LightsDownEyes.From(true, false, true, 0.2, 1, 1).Live);
        Assert.False(LightsDownEyes.From(true, false, true, 0.6, 1, 1).Live);
        Assert.False(LightsDownEyes.From(true, false, false, 0.1, 1, 1).Live);
    }

    [Fact]
    public void Full_screen_rests_in_and_cuts_every_attention_target()
    {
        var run = new LightsDownRun(floating: false);
        var screens = new List<LightsDownScreen> { new(0, 0, 1920, 1080, 1) };
        var cut = new List<LightsDownRect> { new(100, 100, 200, 80) };
        for (int i = 0; i < 20; i++)
            run.StepFull(0.2, screens, (500, 500), false, LightsDownEyes.None, cut, LightsDownTuning.Stock, 0, LightsDownMotion.Full);
        Assert.True(screens[0].Look.Visible);
        var hole = Assert.Single(screens[0].Holes.Current);
        Assert.False(hole.Halo);
        Assert.Equal(1, hole.Presence);
        double rested = run.Full.Level;
        run.StepFull(0.1, screens, (900, 900), false, LightsDownEyes.None, NoCut, LightsDownTuning.Stock, 0, LightsDownMotion.Full);
        Assert.True(run.Full.Level < rested);
    }

    [Fact]
    public void Floating_holes_only_the_screens_a_window_touches_and_follow_pulls_back_when_unwatched()
    {
        var run = new LightsDownRun(floating: true);
        var screens = new List<LightsDownScreen> { new(0, 0, 1920, 1080, 1), new(1920, 0, 1920, 1080, 1) };
        var videos = new List<(long, LightsDownRect)> { (7, new LightsDownRect(200, 200, 640, 360)) };
        for (int i = 0; i < 40; i++)
            run.StepFloating(0.25, screens, videos, false, null, LightsDownEyes.None, LightsDownTuning.Stock, 0, LightsDownMotion.Full, 0x123456);
        Assert.Single(screens[0].Holes.Current);
        Assert.Empty(screens[1].Holes.Current);
        Assert.Equal(0x123456u, screens[0].Look.HaloRgb);
        double grown = run.Float.Grow;
        Assert.True(grown > 0.3);
        run.StepFloating(0.25, screens, videos, true, (5, 5), LightsDownEyes.None, LightsDownTuning.Stock, 0, LightsDownMotion.Full, 0x123456);
        Assert.True(run.Float.Grow < grown);
        Assert.True(run.EndOfVideo());
        Assert.False(new LightsDownRun(false).EndOfVideo());
    }

    [Fact]
    public void Options_motion_and_accent_come_through_the_shared_rules()
    {
        Assert.Equal(LightsDownTuning.Stock, LightsDownRun.TuningFor(null));
        Assert.Equal(LightsDownTuning.Stock, LightsDownRun.TuningFor(new AppSettings()));
        Assert.Equal(LightsDownMotion.Reduced, LightsDownRun.MotionFor(MotionLevel.Reduced));
        Assert.Equal(0xE84393u, LightsDownRun.HaloFor("#E84393"));
        Assert.Equal(LightsDownRun.FallbackHalo, LightsDownRun.HaloFor("nope"));
        Assert.Equal(1.0 / 20, LightsDownRun.FrameSeconds(true));
    }

    [Fact]
    public void The_stack_guard_refuses_a_dim_left_over_a_video_or_an_unreadable_order()
    {
        Assert.Equal(LightsDownStack.Below, LightsDownStackGuard.Order(0, new[] { 2, 5 }, out _));
        Assert.Equal(LightsDownStack.Wrong, LightsDownStackGuard.Order(3, new[] { 2, 5 }, out var lowest));
        Assert.Equal(2, lowest);
        Assert.Equal(LightsDownStack.Unknown, LightsDownStackGuard.Order(-1, new[] { 2 }, out _));
        Assert.Equal(LightsDownStack.Unknown, LightsDownStackGuard.Order(1, new[] { -1 }, out _));
        var g = new LightsDownStackGuard();
        Assert.False(g.Refuse(LightsDownStack.Wrong));
        Assert.False(g.Refuse(LightsDownStack.Below));   // the WM honoured the restack
        Assert.False(g.Refuse(LightsDownStack.Wrong));
        Assert.True(g.Refuse(LightsDownStack.Wrong));
        var u = new LightsDownStackGuard();
        for (int i = 1; i < LightsDownStackGuard.UnknownLimit; i++) Assert.False(u.Refuse(LightsDownStack.Unknown));
        Assert.True(u.Refuse(LightsDownStack.Unknown));
    }
}
