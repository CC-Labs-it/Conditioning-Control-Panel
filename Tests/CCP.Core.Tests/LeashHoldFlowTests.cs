using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Hold-to-cut (WPF MainWindow.Leash.cs LeashHoldSwallows / OnLeashKeyReleased): the first
/// down is the panic press, a release before five seconds never asks, five seconds asks once.</summary>
public class LeashHoldFlowTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Holds for <paramref name="ms"/> with a 250 ms clock (the X11 head's held clock).</summary>
    private static List<LeashHoldStep> Hold(LeashHoldFlow f, int ms, bool leashed = true)
    {
        var steps = new List<LeashHoldStep>();
        for (int t = 0; t <= ms; t += 250) steps.Add(f.Down(T0.AddMilliseconds(t), leashed));
        return steps;
    }

    [Fact]
    public void The_first_down_is_never_swallowed_so_panic_runs()
    {
        var f = new LeashHoldFlow();
        var first = f.Down(T0, leashed: true);
        Assert.False(first.Swallow);
        Assert.False(first.Ask);
        Assert.Null(first.Ring);
        Assert.True(f.Down(T0.AddMilliseconds(250), true).Swallow);
    }

    [Fact]
    public void Releasing_before_five_seconds_never_asks_and_dismisses_the_ring()
    {
        var f = new LeashHoldFlow();
        var steps = Hold(f, 4750);
        Assert.DoesNotContain(steps, s => s.Ask);
        var up = f.Up();
        Assert.True(up.Dismiss);
        Assert.False(up.Ask);
        // A fresh press after the release starts over: four more seconds still do not ask.
        var again = new List<LeashHoldStep>();
        for (int t = 0; t <= 4000; t += 250) again.Add(f.Down(T0.AddSeconds(10).AddMilliseconds(t), true));
        Assert.False(again[0].Swallow);
        Assert.DoesNotContain(again, s => s.Ask);
    }

    [Fact]
    public void A_quick_re_press_after_a_release_starts_a_new_hold()
    {
        var f = new LeashHoldFlow();
        Assert.DoesNotContain(Hold(f, 3000), s => s.Ask);
        f.Up();
        var again = new List<LeashHoldStep>();
        for (int t = 3500; t <= 6500; t += 250) again.Add(f.Down(T0.AddMilliseconds(t), true));
        Assert.False(again[0].Swallow);   // a new press: panic runs
        Assert.DoesNotContain(again, s => s.Ask);
    }

    [Fact]
    public void Five_seconds_held_asks_once_and_the_ring_counts_five_to_one()
    {
        var f = new LeashHoldFlow();
        var steps = Hold(f, 7000);
        Assert.Single(steps, s => s.Ask);
        Assert.Equal(20, steps.FindIndex(s => s.Ask));   // 5000 ms / 250
        var shown = steps.FindAll(s => s.Ring != null).ConvertAll(s => LeashHoldTick.SecondsLeft(s.Ring!.Value));
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, shown);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, steps.FindAll(s => s.Tick != null).ConvertAll(s => s.Tick));
    }

    [Fact]
    public void Not_leashed_never_asks_and_shows_no_ring_but_still_swallows_repeats()
    {
        var f = new LeashHoldFlow();
        var steps = Hold(f, 7000, leashed: false);
        Assert.DoesNotContain(steps, s => s.Ask || s.Ring != null || s.Tick != null);
        Assert.True(steps[1].Swallow);
    }
}
