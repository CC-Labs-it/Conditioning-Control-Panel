using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosBoundPairsTests
{
    private static (ChaosBoundPairs pairs, AmbientBubble a, AmbientBubble b) Pair()
    {
        var (sa, sb) = ChaosBubbleVariants.BuildBoundPair(1, 1, 1, 1);
        var a = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1, sa);
        var b = AmbientBubble.SpawnChaos(new Random(2), 0, 0, 0, 1920, 1080, 1, sb);
        a.FuseRemainingMs = b.FuseRemainingMs = 4000;
        var pairs = new ChaosBoundPairs();
        pairs.Add(sa.PairId, a, b);
        return (pairs, a, b);
    }

    [Fact] public void Bound_channels_cost_half_and_free_snaps_cost_nothing()
    {
        var (bound, _) = ChaosBubbleVariants.BuildBoundPair(1);
        var plain = new EffectBubbleSpec { IsLive = true };
        Assert.Equal(15, ChaosFieldMath.DefuseCostFor(bound));
        Assert.Equal(30, ChaosFieldMath.DefuseCostFor(plain));
        var state = new ChaosRunState(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
        state.Focus = 100;
        ChaosFieldMath.ApplyDefuse(state, bound, 3, true, false, 1, 1);
        Assert.Equal(85, state.Focus);
        ChaosFieldMath.ApplyDefuse(state, bound, 3, true, true, 1, 1);
        ChaosFieldMath.ApplyDefuse(state, bound, 3, false, false, 1, 1);
        Assert.Equal(85, state.Focus);
        ChaosFieldMath.ApplyDefuse(state, plain, 3, true, false, 1, 1);
        Assert.Equal(55, state.Focus);
    }

    [Fact] public void Both_resolve_inside_window()
    {
        var (p, a, b) = Pair(); var now = DateTime.UtcNow; int id = a.Spec!.PairId;
        p.Resolve(id, a, true, now); p.Resolve(id, b, true, now.AddMilliseconds(2500));
        Assert.Empty(p.Step(now.AddSeconds(3), false)); Assert.Equal(4000, b.FuseRemainingMs);
    }
    [Fact] public void Lapse_enrages_survivor()
    {
        var (p, a, b) = Pair(); var now = DateTime.UtcNow;
        p.Resolve(a.Spec!.PairId, a, true, now);
        Assert.Empty(p.Step(now.AddMilliseconds(2500), false));
        Assert.Same(b, Assert.Single(p.Step(now.AddMilliseconds(2501), false)));
        Assert.Equal(2000, b.FuseRemainingMs);
    }
    [Fact] public void Detonation_enrages_partner()
    {
        var (p, a, b) = Pair(); var now = DateTime.UtcNow;
        Assert.Same(b, p.Resolve(a.Spec!.PairId, a, false, now));
        Assert.Equal(2000, b.FuseRemainingMs); Assert.Empty(p.Step(now.AddSeconds(4), false));
    }
    [Fact] public void Frozen_tick_shifts_window()
    {
        var (p, a, b) = Pair(); var now = DateTime.UtcNow;
        p.Resolve(a.Spec!.PairId, a, true, now);
        p.Step(now.AddMilliseconds(2500), true);
        Assert.Empty(p.Step(now.AddMilliseconds(2532), false));
        Assert.Same(b, Assert.Single(p.Step(now.AddMilliseconds(2533), false)));
    }
    [Fact] public void Enrage_speeds_the_survivor_and_floors_the_fuse()
    {
        var (_, _, b) = Pair();
        var bY = b.Y;
        b.FuseRemainingMs = 1000; b.Enrage();
        b.Step();
        Assert.Equal(568, b.FuseRemainingMs);
        // Identical seeds isolate the speed multiplier.
        var copy = AmbientBubble.SpawnChaos(new Random(2), 0, 0, 0, 1920, 1080, 1, b.Spec!);
        var copyY = copy.Y; copy.Step();
        Assert.Equal((copyY - copy.Y) * 1.4, bY - b.Y, 8);
    }
    [Fact] public void Clear_drops_all_pairs()
    {
        var (p, a, b) = Pair(); var now = DateTime.UtcNow;
        p.Resolve(a.Spec!.PairId, a, true, now); p.Clear();
        Assert.Empty(p.Step(now.AddSeconds(4), false)); Assert.Equal(4000, b.FuseRemainingMs);
    }
}
