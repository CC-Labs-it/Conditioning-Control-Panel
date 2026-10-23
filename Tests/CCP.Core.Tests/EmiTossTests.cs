using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The toss maths shared by both desks (WPF EmiDeskWindow.Toss.cs).</summary>
public sealed class EmiTossTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const double Keep = 0.78;   // the wobble's low-pass

    /// <summary>Drag her straight down at <paramref name="dipPerSec"/> for <paramref name="ms"/>,
    /// 16 ms frames, starting at T0. Returns the time of the last frame.</summary>
    private static DateTime DragDown(EmiFlingDetector d, double dipPerSec, int ms, double y0 = 0)
    {
        d.PickUp(y0);
        double y = y0;
        DateTime now = T0;
        for (int t = 16; t <= ms; t += 16)
        {
            now = T0.AddMilliseconds(t);
            y += dipPerSec * 0.016;
            d.Frame(y, 0.016, 0, Keep, now);
        }
        return now;
    }

    [Fact]
    public void A_fast_long_drag_is_a_throw_and_a_short_or_slow_one_is_not()
    {
        var d = new EmiFlingDetector();
        var end = DragDown(d, 5000, 400);
        Assert.True(d.PutDown(end));
        Assert.False(d.PutDown(end));                       // put down resets

        end = DragDown(d, 5000, 96);                        // under FLING_MS once up to speed
        Assert.False(d.PutDown(end));

        end = DragDown(d, 800, 400);                        // never fast enough
        Assert.False(d.PutDown(end));
    }

    [Fact]
    public void A_let_go_inside_the_grace_still_counts_and_after_it_does_not()
    {
        var d = new EmiFlingDetector();
        var end = DragDown(d, 5000, 400);
        Assert.True(d.PutDown(end.AddMilliseconds(EmiTossRules.FlingGraceMs - 1)));

        end = DragDown(d, 5000, 400);
        Assert.False(d.PutDown(end.AddMilliseconds(EmiTossRules.FlingGraceMs)));
    }

    [Fact]
    public void Slowing_down_restarts_the_hold()
    {
        var d = new EmiFlingDetector();
        d.PickUp(0);
        d.Frame(100, 0.016, 5000, Keep, T0);                 // fast (horizontal) from T0
        d.Frame(100, 0.016, 0, Keep, T0.AddMilliseconds(60));   // stopped: the hold resets
        d.Frame(100, 0.016, 5000, Keep, T0.AddMilliseconds(100));
        d.Frame(100, 0.016, 5000, Keep, T0.AddMilliseconds(200)); // only 100 ms fast since
        Assert.False(d.PutDown(T0.AddMilliseconds(201)));
    }

    [Fact]
    public void The_landing_hits_the_css_keyframes_and_rests_at_one()
    {
        Assert.Equal((1.0, 1.0), EmiTossRules.LandingScale(false, 0));
        Assert.Equal((1.0, 1.0), EmiTossRules.LandingScale(true, 0));
        AssertNear((1.07, 0.9475), EmiTossRules.LandingScale(false, 0.20));   // CubicEase EaseOut(0.5) = 0.875
        AssertNear((1.07984, 0.94012), EmiTossRules.LandingScale(false, 0.35)); // still easing in
        AssertNear((1.08, 0.94), EmiTossRules.LandingScale(false, 0.40));
        AssertNear((0.95, 1.06), EmiTossRules.LandingScale(true, 0.20));
        AssertNear((1.10, 0.88), EmiTossRules.LandingScale(true, 0.55));
        AssertNear((1.0, 1.0), EmiTossRules.LandingScale(false, 1));
        AssertNear((1.0, 1.0), EmiTossRules.LandingScale(true, 1));
        // Half way through the ordinary landing's spring back (0.4 + 0.3): WPF ElasticEase
        // EaseOut(0.5) with Oscillations 1 / Springiness 4 is 1.0843, an overshoot past rest.
        AssertNear((1.08 + (1 - 1.08) * 1.0843, 0.94 + (1 - 0.94) * 1.0843), EmiTossRules.LandingScale(false, 0.70));
    }

    [Theory]
    [InlineData(100, "top")]
    [InlineData(500, "mid")]
    [InlineData(1000, "bottom")]
    public void Zone_rows_are_thirds(double y, string row) => Assert.Equal(row, EmiTossRules.ZoneRow(y, 0, 1050));

    private static void AssertNear((double X, double Y) want, (double X, double Y) got)
    {
        Assert.True(Math.Abs(want.X - got.X) < 1e-3 && Math.Abs(want.Y - got.Y) < 1e-3, $"want {want} got {got}");
    }
}
