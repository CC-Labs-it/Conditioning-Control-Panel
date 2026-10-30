using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FloatingVideoMotionTests
{
    private static FloatingVideoMotion New() => new(100, 100, 200, 100, 0, 0, 1000, 800);
    private static void Throw(FloatingVideoMotion m, MotionLevel level = MotionLevel.Full)
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
}
