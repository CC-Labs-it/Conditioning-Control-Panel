using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosEStimChargeTests
{
    private static AmbientBubble Bubble(double x, EffectBubbleSpec? spec = null) =>
        new() { X = x, Size = 20, Spec = spec ?? new EffectBubbleSpec() };

    [Fact]
    public void Charge_waits_for_a_valid_click_with_a_conductor_then_uses_three_nearest()
    {
        var field = new AmbientBubbleField();
        var source = Bubble(0);
        field.Bubbles.Add(source);
        field.ArmEStim(5, true);
        field.ArmEStim(2, false);
        Assert.Equal(2, field.EStimChargesLeft); // rearming replaces the old charge and fork mode
        Assert.Empty(field.ConductEStim(source));
        Assert.Equal(2, field.EStimChargesLeft);
        var live = Bubble(1, new EffectBubbleSpec { IsLive = true });
        var rabbit = Bubble(1, new EffectBubbleSpec { IsDarter = true });
        var freeze = Bubble(1, new EffectBubbleSpec { IsFreeze = true });
        Assert.Empty(field.ConductEStim(live));
        Assert.Empty(field.ConductEStim(rabbit));
        Assert.Empty(field.ConductEStim(freeze));
        var near = Bubble(100);
        var middle = Bubble(200, new EffectBubbleSpec { IsLive = true });
        var boundary = Bubble(600);
        var far = Bubble(601);
        var ignored = new[] { rabbit, freeze, Bubble(2, new EffectBubbleSpec { IsTease = true }),
            Bubble(3, new EffectBubbleSpec { IsBrittle = true }), Bubble(4, new EffectBubbleSpec { IsChaperoneLive = true }),
            new AmbientBubble { X = 5, Size = 20 }, new AmbientBubble { X = 6, Size = 20, Spec = new(), Popping = true } };
        field.Bubbles.AddRange(ignored);
        field.Bubbles.AddRange(new[] { far, boundary, middle, near });
        var hits = field.ConductEStim(source);
        Assert.Equal(new[] { near, middle, boundary }, hits.Select(h => h.Target));
        Assert.Equal(new[] { 70, 140, 210 }, hits.Select(h => h.DelayMs));
        Assert.All(hits, h => Assert.Same(source, h.From));
        Assert.Equal(1, field.EStimChargesLeft);
        field.HoldChaos = true;
        Assert.Empty(field.ConductEStim(source));
        Assert.Equal(1, field.EStimChargesLeft);
        field.ResetEStim();
        field.HoldChaos = false;
        Assert.Empty(field.ConductEStim(source));
    }

    [Fact]
    public void Capstone_ripples_breadth_first_and_stops_after_forty_hits()
    {
        var field = new AmbientBubbleField();
        var source = Bubble(0);
        var first = Bubble(500);
        var second = Bubble(1000);
        var third = Bubble(1500);
        field.Bubbles.AddRange(new[] { source, first, second, third });
        field.ArmEStim(1, true);
        var hits = field.ConductEStim(source);
        Assert.Equal(new[] { first, second, third }, hits.Select(h => h.Target));
        Assert.Equal(new[] { source, first, second }, hits.Select(h => h.From));
        Assert.Equal(new[] { 70, 140, 210 }, hits.Select(h => h.DelayMs));
        Assert.Equal(0, field.EStimChargesLeft);
        Assert.Empty(field.ConductEStim(source));
        field.ArmEStim(0, true);
        field.Bubbles.Clear();
        field.Bubbles.Add(source);
        field.Bubbles.AddRange(Enumerable.Range(1, 50).Select(x => Bubble(x)));
        Assert.Equal(40, field.ConductEStim(source).Count);
        Assert.Equal(0, field.EStimChargesLeft);
        field.ArmEStim(1, false);
        Assert.Equal(3, field.ConductEStim(source).Count);
    }
}
