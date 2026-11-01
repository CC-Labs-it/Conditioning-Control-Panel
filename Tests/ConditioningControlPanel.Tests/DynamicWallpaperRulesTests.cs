using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The moving wallpaper's rules that need no GPU: panic never waits on it, what it keeps in
/// memory, how Gentle slows it, and when a window pauses it.</summary>
public class DynamicWallpaperRulesTests
{
    // ---- the moving wallpaper's budgets

    [Theory]
    [InlineData(2, 1)]
    [InlineData(120, 1)]
    [InlineData(121, 2)]
    [InlineData(240, 2)]
    [InlineData(600, 5)]
    public void A_long_gif_keeps_every_nth_frame(int frames, int stride)
    {
        Assert.Equal(stride, DynamicFrameBudget.Stride(frames));
        Assert.True(frames / stride <= DynamicFrameBudget.MaxFramesKept);
    }

    [Fact]
    public void Frames_that_do_not_fit_the_budget_stay_still()
    {
        long cost = DynamicFrameBudget.Cost(320, 320, 100);
        Assert.Equal(320L * 320 * 4 * 100, cost);
        Assert.True(DynamicFrameBudget.MayMove(100, cost, cost));
        Assert.False(DynamicFrameBudget.MayMove(100, cost, cost - 1));
        Assert.False(DynamicFrameBudget.MayMove(1, 0, cost));
        Assert.False(DynamicFrameBudget.MayMove(DynamicFrameBudget.MaxSourceFrames + 1, 0, cost));
    }

    [Fact]
    public void Gentle_speed_stretches_the_frame_delays()
    {
        var delays = new[] { 100, 100 };
        int cur = 0;
        double clock = 0;
        Assert.False(DynamicFrameBudget.Advance(delays, ref cur, ref clock, 100, 0.5));   // 200 ms at half speed
        Assert.True(DynamicFrameBudget.Advance(delays, ref cur, ref clock, 100, 0.5));
        Assert.Equal(1, cur);
    }

    [Fact]
    public void A_still_tile_never_reports_a_change()
    {
        int cur = 0;
        double clock = 0;
        Assert.False(DynamicFrameBudget.Advance(new[] { 0 }, ref cur, ref clock, 1000, 1));
    }

    [Theory]
    //          left, top, right, bottom, zoomed, covered
    [InlineData(0, 0, 1920, 1080, false, true)]      // true fullscreen
    [InlineData(-8, -8, 1928, 1048, true, true)]     // maximised: stops at the taskbar
    [InlineData(-8, -8, 1928, 1048, false, false)]   // the same size but not zoomed leaves the taskbar edge open
    [InlineData(100, 100, 900, 700, false, false)]   // an ordinary window
    public void Only_a_window_hiding_the_whole_monitor_pauses_the_draw(int l, int t, int r, int b, bool zoomed, bool covered)
        => Assert.Equal(covered, DynamicFrameBudget.Covers(l, t, r, b, zoomed, 0, 0, 1920, 1080));
}
