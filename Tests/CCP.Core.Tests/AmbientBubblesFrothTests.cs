using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Core.Tests;

public class AmbientBubblesFrothTests
{
    private static AmbientBubble Bubble(double x = 0) => new() { X = x, Y = 0, Size = 100, Clickable = true };
    private static void NoPop(AmbientBubble b) => throw new InvalidOperationException("unexpected pop");

    [Fact]
    public void Off_does_not_change_field_or_bubbles()
    {
        var field = new AmbientBubbleField();
        var a = Bubble(); var b = Bubble();
        field.Bubbles.AddRange(new[] { a, b });
        var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var before = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(field.Bubbles, options);
        field.StepFroth(false, .03, 45, 2.5, NoPop);
        Assert.Equal(before, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(field.Bubbles, options));
        Assert.Equal(new[] { a, b }, field.Bubbles);
        Assert.Equal((0, 0, 100, 1, 0), (a.X, a.Y, a.Size, a.FrothShown, a.FrothRim));
        Assert.Equal(1, a.FrothPartCount);
    }

    [Fact]
    public void Taking_parts_from_a_plain_bubble_leaves_froth_state_untouched()
    {
        var field = new AmbientBubbleField();
        var bubble = Bubble();
        Assert.Empty(field.FrothTakeParts(bubble));
        Assert.False(bubble.FrothPartsPaid);
    }

    [Fact]
    public void Touching_bubbles_merge_cap_ripen_and_keep_one_credit_per_part()
    {
        var field = new AmbientBubbleField(new Random(1));
        field.Bubbles.AddRange(Enumerable.Range(0, 10).Select(_ => Bubble()));
        field.StepFroth(true, .03, 45, 2.5, NoPop);
        var carrier = Assert.Single(field.Bubbles);
        Assert.Equal(10, carrier.FrothPartCount);
        Assert.Equal(112.5, carrier.FrothRadius, 6);
        for (int i = 0; i < 30; i++) field.StepFroth(true, .03, 45, 2.5, NoPop);
        Assert.Equal(1, carrier.FrothRim, 6);
        Assert.True(carrier.FrothShown > 2.4);
        var parts = field.FrothTakeParts(carrier);
        Assert.Equal(9, parts.Count);
        Assert.Empty(field.FrothTakeParts(carrier));
        var settings = new AppSettings();
        Assert.Equal(5, field.Pop(carrier, settings));
        Assert.Equal(45, parts.Sum(part => field.Pop(part, settings)));
        Assert.Equal(0, field.Pop(carrier, settings));
    }

    [Fact]
    public void Merged_size_obeys_player_cap()
    {
        var field = new AmbientBubbleField();
        field.Bubbles.AddRange(Enumerable.Range(0, 10).Select(_ => Bubble()));
        field.StepFroth(true, .03, 45, 2.5, NoPop);
        Assert.Equal(112.5, Assert.Single(field.Bubbles).FrothRadius, 6);
    }

    [Fact]
    public void Ripe_bubble_gets_a_gold_rim()
    {
        var field = new AmbientBubbleField();
        field.Bubbles.AddRange(Enumerable.Range(0, 6).Select(_ => Bubble()));
        for (int i = 0; i < 30; i++) field.StepFroth(true, .03, 45, 4.5, NoPop);
        Assert.True(Assert.Single(field.Bubbles).FrothRim > .99);
    }

    [Fact]
    public void Absorbed_parts_are_returned_once_for_credit()
    {
        var field = new AmbientBubbleField();
        field.Bubbles.AddRange(new[] { Bubble(), Bubble() });
        field.StepFroth(true, .03, 45, 4.5, NoPop);
        var carrier = Assert.Single(field.Bubbles);
        Assert.Single(field.FrothTakeParts(carrier));
        Assert.Empty(field.FrothTakeParts(carrier));
    }

    [Fact]
    public void Visual_growth_does_not_enlarge_the_input_disc()
    {
        var field = new AmbientBubbleField();
        field.Bubbles.AddRange(new[] { Bubble(), Bubble() });
        for (int i = 0; i < 30; i++) field.StepFroth(true, .03, 45, 4.5, NoPop);
        var carrier = Assert.Single(field.Bubbles);
        Assert.True(carrier.FrothShown > 1);
        Assert.Equal(100, carrier.HitSize);
    }

    [Fact]
    public void Turning_off_after_credit_removes_the_popping_carrier()
    {
        var field = new AmbientBubbleField();
        field.Bubbles.AddRange(new[] { Bubble(), Bubble() });
        field.StepFroth(true, .03, 45, 4.5, NoPop);
        var carrier = Assert.Single(field.Bubbles);
        Assert.Single(field.FrothTakeParts(carrier));
        field.Pop(carrier, new AppSettings());
        field.StepFroth(false, .03, 45, 4.5, NoPop);
        Assert.Empty(field.Bubbles);
    }

    [Fact]
    public void Blast_reaches_only_neighbours_on_same_screen_after_delay_once()
    {
        var field = new AmbientBubbleField();
        var near = Bubble(140); var far = Bubble(500); var otherScreen = Bubble(140); otherScreen.Screen = 1;
        field.Bubbles.AddRange(new[] { near, far, otherScreen });
        field.StepFroth(true, .03, 45, 4.5, NoPop);
        field.FrothBlast(0, 50, 50, 100, 45);
        int pops = 0;
        for (int i = 0; i < 20; i++) field.StepFroth(true, .03, 45, 4.5, b => { b.Popping = true; pops++; });
        Assert.Equal(1, pops); Assert.True(near.Popping); Assert.False(far.Popping); Assert.False(otherScreen.Popping);
    }

    [Fact]
    public void Disabling_froth_discards_merged_carriers_without_pop()
    {
        var field = new AmbientBubbleField();
        field.Bubbles.AddRange(new[] { Bubble(), Bubble(), Bubble(500) });
        field.StepFroth(true, .03, 45, 4.5, NoPop);
        Assert.Equal(2, field.Bubbles.Count);
        field.StepFroth(false, .03, 45, 4.5, NoPop);
        var survivor = Assert.Single(field.Bubbles);
        Assert.Equal(1, survivor.FrothShown); Assert.Equal(0, survivor.FrothRim);
    }
}
