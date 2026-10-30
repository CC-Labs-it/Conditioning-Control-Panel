using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FloatingVideoMotionTests
{
    private static FloatingVideoMotionModel New() => new(100, 100, 200, 100, 0, 0, 1000, 800);
    private static void Throw(FloatingVideoMotionModel m, MotionLevel level = MotionLevel.Full)
    {
        m.Press(110, 110, 0);
        m.Move(210, 110, 20);
        m.Step(.02);
        Assert.Equal(level == MotionLevel.Off ? FlashDragOutcome.Place : FlashDragOutcome.Fling, m.Release(20, level));
    }

    [Fact]
    public void Throw_is_scaled_capped_and_changes_speed_in_flight()
    {
        var m = New();
        m.Speed = .1;
        Throw(m);
        Assert.Equal(200, m.X); // Drag stays 1:1, even at the lowest speed.
        Assert.Equal(360, m.Vx, 6);
        m.Speed = .2;
        Assert.Equal(720, m.Vx, 6);
        m.Step(.01);
        Assert.True(m.X > 200);
        m.Speed = double.NaN;
        Assert.Equal(1, m.Speed);
    }

    [Fact]
    public void Held_window_crosses_to_negative_monitor_and_rehomes_on_release_and_unplug()
    {
        var m = New();
        m.SetDesktop(-1000, 0, 2000, 800);
        m.MonitorAt = (x, _) => x < 0 ? (-1000, 0, 1000, 800) : (0, 0, 1000, 800);
        m.Press(110, 110, 0);
        m.Move(-390, 110, 20);
        m.Step(.02);
        Assert.Equal(-400, m.X);
        m.Release(20, MotionLevel.Off);
        Assert.Equal(-1000, m.Bounds.X);
        Assert.Equal(-400, m.X);
        m.MonitorAt = (_, _) => (0, 0, 1000, 800);
        m.Rehome();
        Assert.Equal(0, m.X);
    }

    [Fact]
    public void Resize_keeps_grab_point_and_bounce_stays_inside_then_drop_stops()
    {
        var m = New();
        m.Press(150, 125, 0);
        m.Resize(400, 200);
        Assert.Equal(50, m.X);
        Assert.Equal(75, m.Y);
        m.Drop();
        Throw(m);
        for (var i = 0; i < 500; i++)
        {
            m.Step(.01);
            Assert.InRange(m.X, 0, 600);
            Assert.InRange(m.Y, 0, 600);
        }
        m.Drop();
        Assert.False(m.Active);
        Assert.False(m.Step(.02));
    }

    [Theory]
    [InlineData(MotionLevel.Off)]
    [InlineData(MotionLevel.Reduced)]
    public void Motion_preferences_limit_throw(MotionLevel level)
    {
        var m = New();
        Throw(m, level);
        Assert.True(Math.Abs(m.Vx) <= 1800);
        if (level == MotionLevel.Off) Assert.False(m.Flying);
    }
    [Fact]
    public void A_fast_drag_is_thrown_the_way_the_hand_went_not_the_way_its_last_twitch_did()
    {
        // WPF regression: a 1 kHz rightward drag ends with a 2 px leftward twitch.
        var m = New();
        m.Press(650, 350, 0);
        double x = 650;
        for (int ms = 1; ms <= 60; ms++) { x += 3; m.Move(x, 350, ms); }
        m.Move(x - 2, 350, 60.05);
        Assert.Equal(FlashDragOutcome.Fling, m.Release(60.1, MotionLevel.Full));
        Assert.True(m.Vx > 0, $"thrown backwards: {m.Vx}");
        Assert.Equal(0, m.Vy, 6);
    }

    [Fact]
    public void A_hand_that_stopped_before_letting_go_places_the_window()
    {
        var m = New();
        m.Press(650, 350, 0);
        for (int ms = 1; ms <= 40; ms++) m.Move(650 + ms * 3, 350, ms);
        Assert.Equal(FlashDragOutcome.Place, m.Release(400, MotionLevel.Full));
        Assert.False(m.Active);
    }

}
