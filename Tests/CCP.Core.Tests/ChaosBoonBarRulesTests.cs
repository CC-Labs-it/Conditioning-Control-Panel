using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The run-pick ribbon rules (WPF ChaosBoonBarOverlay): pick order, only newcomers pop in,
/// staggered 70 ms, right-anchored left of the clock.</summary>
public class ChaosBoonBarRulesTests
{
    [Fact]
    public void Tiles_keep_pick_order_and_only_newcomers_pop_in_staggered()
    {
        var s = new ChaosRunState(new ChaosRunConfig());
        var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
        var curse = ChaosBoonPool.All.First(b => b.IsCurse);
        s.ApplyBoon(curse);
        s.ApplyBoon(boon);
        var tiles = ChaosBoonBarRules.Snapshot(s.RunPickTiles);
        Assert.Equal(new[] { curse.Name, boon.Name }, tiles.Select(t => t.Name));
        Assert.True(tiles[0].IsCurse);

        // 1 tile was shown, 3 now: index 0 stays, 1 and 2 pop in as order 0 and 1.
        Assert.Equal(new[] { -1, 0, 1 }, Enumerable.Range(0, 3).Select(i => ChaosBoonBarRules.PopOrder(i, 1)));
        Assert.Equal(0, ChaosBoonBarRules.PopOrder(0, 0));
    }

    [Fact]
    public void Pop_frame_follows_the_wpf_timeline()
    {
        Assert.Equal((1d, 1d), ChaosBoonBarRules.PopFrame(-1, 0));            // already shown: settled
        var (s0, o0) = ChaosBoonBarRules.PopFrame(1, 70);                    // second newcomer waits 70 ms
        Assert.Equal(0.4, s0, 9);
        Assert.Equal(0, o0);
        var (_, fade) = ChaosBoonBarRules.PopFrame(0, 95);
        Assert.Equal(0.5, fade, 6);                                           // fade is 190 ms
        Assert.True(ChaosBoonBarRules.PopFrame(0, 250).Scale > 1.0);          // BackEase overshoot
        Assert.Equal((1d, 1d), ChaosBoonBarRules.PopFrame(2, 140 + 340));     // lands at delay + 340 ms
        Assert.False(ChaosBoonBarRules.PopDone(3, 140 + 339));
        Assert.True(ChaosBoonBarRules.PopDone(3, 140 + 340));
        Assert.True(ChaosBoonBarRules.PopDone(0, 0));
    }

    [Fact]
    public void Ribbon_is_right_anchored_left_of_the_clock()
    {
        Assert.Equal(1920 - 220 - 100, ChaosBoonBarRules.Left(1920, 100));
        Assert.Equal(1920 - 220 - 148, ChaosBoonBarRules.Left(1920, 148));   // grows leftward
    }
}
