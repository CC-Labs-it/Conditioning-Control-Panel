using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosSnapHazardsTests
{
    private static AmbientBubble Bubble(double x, EffectBubbleSpec? spec) => new() { X = x, Spec = spec };
    private static void Step(ChaosSnapHazards h, AmbientBubble[] bubbles, DateTime now, bool frozen = false,
        Action<double, double, AmbientBubble>? strike = null) => h.Step(bubbles, now, frozen,
            b => (b.CenterX, b.CenterY), b => b.Popping = true, strike ?? ((_, _, _) => { }));

    [Fact]
    public void Ripple_expands_32ms_per_tick_pops_only_treats_and_expires()
    {
        var h = new ChaosSnapHazards();
        var near = Bubble(25, new()); var far = Bubble(430, new());
        var outside = Bubble(431, new()); var live = Bubble(1, new() { IsLive = true });
        var rabbit = Bubble(1, new() { IsDarter = true }); var freeze = Bubble(1, new() { IsFreeze = true });
        var ambient = Bubble(1, null);
        var all = new[] { near, far, outside, live, rabbit, freeze, ambient };
        h.Ripple(0, 0);
        Step(h, all, DateTime.UnixEpoch, true); Assert.False(near.Popping);
        Step(h, all, DateTime.UnixEpoch); Assert.True(near.Popping); Assert.False(far.Popping);
        for (int i = 0; i < 17; i++) Step(h, all, DateTime.UnixEpoch);
        Assert.True(far.Popping);
        foreach (var b in new[] { outside, live, rabbit, freeze, ambient }) Assert.False(b.Popping);
        near.Popping = false;
        Step(h, all, DateTime.UnixEpoch); Assert.False(near.Popping);
    }

    [Fact]
    public void Residue_hits_boundary_and_lives_strikes_once_and_expires_on_wall_time()
    {
        var h = new ChaosSnapHazards(); var now = DateTime.UnixEpoch;
        var live = Bubble(170, new() { IsLive = true }); var outside = Bubble(171, new());
        var rabbit = Bubble(1, new() { IsDarter = true }); var freeze = Bubble(1, new() { IsFreeze = true });
        var all = new[] { live, outside, rabbit, freeze }; int strikes = 0;
        h.Residue(0, 0, now);
        Step(h, all, now, true); Assert.False(live.Popping);
        Step(h, all, now.AddMilliseconds(1999), strike: (x, y, b) =>
        { Assert.Equal((0d, 0d), (x, y)); Assert.Same(live, b); strikes++; });
        Assert.True(live.Popping); Assert.Equal(1, strikes);
        Assert.False(outside.Popping); Assert.False(rabbit.Popping); Assert.False(freeze.Popping);
        live.Popping = false;
        Step(h, all, now.AddMilliseconds(2000)); Assert.False(live.Popping);
        h.Ripple(170, 0); h.Residue(170, 0, now); h.Clear();
        Step(h, all, now); Assert.False(live.Popping);
    }
}
