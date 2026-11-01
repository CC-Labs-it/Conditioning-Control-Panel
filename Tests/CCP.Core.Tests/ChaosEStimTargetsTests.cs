using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF BubbleService.cs:373-423: range, ordering, immunity and chain timing.</summary>
public class ChaosEStimTargetsTests
{
    private static AmbientBubble At(double x, EffectBubbleSpec? spec = null) => new()
        { X = x, Y = 0, Size = 10, Spec = spec ?? new EffectBubbleSpec() };

    [Fact]
    public void Normal_arcs_choose_three_nearest_in_range_and_skip_immune_bubbles()
    {
        var f = new AmbientBubbleField();
        var source = At(0);
        var a = At(50); var b = At(100); var c = At(600); var extra = At(601);
        f.Bubbles.AddRange(new[] { source, c, extra, b, a, At(10, new() { IsDarter = true }),
            At(11, new() { IsFreeze = true }), At(12, new() { IsTease = true }),
            At(13, new() { IsBrittle = true }), At(14, new() { IsLive = true, IsChaperoneLive = true }),
            new AmbientBubble { X = 15 }, new AmbientBubble { X = 16, Popping = true, Spec = new() } });
        var hits = f.EStimTargets(source, false);
        Assert.Equal(new[] { a, b, c }, hits.Select(h => h.Target));
        Assert.Equal(new[] { 70, 140, 210 }, hits.Select(h => h.DelayMs));
        Assert.All(hits, h => Assert.Same(source, h.From));
    }

    [Fact]
    public void Capstone_ripples_breadth_first_with_hop_delay_and_forty_target_cap()
    {
        var f = new AmbientBubbleField();
        var source = At(0); var first = At(500); var second = At(1000); var far = At(1601);
        f.Bubbles.AddRange(new[] { source, first, second, far });
        var hits = f.EStimTargets(source, true);
        Assert.Equal(new[] { first, second }, hits.Select(h => h.Target));
        Assert.Equal(new[] { source, first }, hits.Select(h => h.From));
        Assert.Equal(new[] { 70, 140 }, hits.Select(h => h.DelayMs));
        for (int i = 0; i < 50; i++) f.Bubbles.Add(At(20 + i));
        Assert.Equal(40, f.EStimTargets(source, true).Count);
    }
}
