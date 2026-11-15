using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AmbientBubbleEggTests
{
    private sealed class Roll : Random
    {
        private readonly int _value;
        public int Calls;
        public Roll(int value) => _value = value;
        public override int Next(int maxValue) { Calls++; return _value; }
    }

    private static AmbientBubble Bubble(string variant = "flash") => new()
    {
        AmbientTrigger = true, Clickable = true, Size = 100,
        Spec = new EffectBubbleSpec { VariantId = variant, TreatLifeMs = 5000 }
    };

    [Fact]
    public void ClaimPausesLifeAndBlocksPlayerPopUntilReleased()
    {
        var b = Bubble();
        for (int i = 0; i < 126; i++) b.Step();
        Assert.True(b.TryClaimForAvatar(new Roll(0)));
        var field = new AmbientBubbleField(new Random(1));
        field.Bubbles.Add(b);
        double x = b.X, y = b.Y;
        for (int i = 0; i < 200; i++) b.Step();
        Assert.Equal(x, b.X); Assert.Equal(y, b.Y);
        Assert.False(b.Popping);
        Assert.Null(field.HitTest(b.Screen, b.CenterX, b.CenterY));
        Assert.Equal(0, field.Pop(b, new AppSettings()));
        Assert.False(b.Popping);
        b.ReleaseAvatarClaim();
        for (int i = 0; i < 31; i++) b.Step();
        Assert.True(b.Popping);
    }

    [Fact]
    public void EligibleBubbleRollsOnlyOnceAfterFourSeconds()
    {
        var b = Bubble(); var roll = new Roll(99);
        for (int i = 0; i < 125; i++) b.Step();
        Assert.False(b.TryClaimForAvatar(roll)); Assert.Equal(0, roll.Calls);
        b.Step();
        Assert.False(b.TryClaimForAvatar(roll));
        Assert.False(b.TryClaimForAvatar(roll)); Assert.Equal(1, roll.Calls);
    }

    [Theory]
    [InlineData("video")]
    [InlineData("htlink")]
    public void FullscreenPayloadsCannotBeClaimed(string variant)
    {
        var b = Bubble(variant); var roll = new Roll(0);
        for (int i = 0; i < 126; i++) b.Step();
        Assert.False(b.TryClaimForAvatar(roll)); Assert.Equal(0, roll.Calls);
    }
}
