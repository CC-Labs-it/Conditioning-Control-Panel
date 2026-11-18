using System;
using System.IO;
using Newtonsoft.Json.Linq;
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
        Spec = new EffectBubbleSpec { VariantId = variant, TreatLifeMs = 5000, Payload = new SubliminalPayload { Ambient = true } }
    };

    [Fact]
    public void ClaimPausesLifeAndBlocksPlayerPopUntilReleased()
    {
        var b = Bubble();
        for (int i = 0; i < 126; i++) b.Step();
        Assert.True(b.TryClaimForAvatar(new Roll(0), DateTime.UtcNow.AddSeconds(5)));
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
        Assert.False(b.TryClaimForAvatar(roll, DateTime.UtcNow.AddSeconds(3))); Assert.Equal(0, roll.Calls);
        b.Step();
        Assert.False(b.TryClaimForAvatar(roll, DateTime.UtcNow.AddSeconds(5)));
        Assert.False(b.TryClaimForAvatar(roll, DateTime.UtcNow.AddSeconds(5))); Assert.Equal(1, roll.Calls);
    }

    [Theory]
    [InlineData("video")]
    [InlineData("htlink")]
    public void FullscreenPayloadsCannotBeClaimed(string variant)
    {
        var b = Bubble(variant); var roll = new Roll(0);
        for (int i = 0; i < 126; i++) b.Step();
        Assert.False(b.TryClaimForAvatar(roll, DateTime.UtcNow.AddSeconds(5))); Assert.Equal(0, roll.Calls);
    }

    [Fact]
    public void AvatarPopFallbackHasAnEntryInEachLanguage()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
            Assert.False(string.IsNullOrWhiteSpace(JObject.Parse(File.ReadAllText(file))["bubble_avatar_pop_fallback"]?.Value<string>()),
                Path.GetFileName(file));
    }
}
