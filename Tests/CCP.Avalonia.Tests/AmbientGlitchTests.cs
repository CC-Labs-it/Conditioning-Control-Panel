using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AmbientGlitchTests
{
    private sealed class Rolls : Random
    {
        private readonly int _roll;
        public Rolls(int roll) => _roll = roll;
        public override int Next(int maxValue) => maxValue == 100 ? _roll : 0;
    }

    [Fact]
    public void TriggerRespectsSwitchChanceSelectionAndCap()
    {
        var s = new AppSettings { BubbleTriggersEnabled = true, BubbleTriggerChance = 25,
            BubbleTriggerVariants = new List<string> { "glitch" } };
        Assert.Null(BubbleOverlay.RollGlitch(s, new Rolls(25), 0));
        Assert.Null(BubbleOverlay.RollGlitch(s, new Rolls(0), 4));
        var spec = Assert.IsType<EffectBubbleSpec>(BubbleOverlay.RollGlitch(s, new Rolls(24), 3));
        Assert.Equal("glitch", spec.VariantId);
        Assert.Equal(200, spec.SizePx);
        Assert.Equal(0xFF9A40FFu, spec.Tint);
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", "bubbles", "glitch.png")));
        var payload = Assert.IsType<OverlayPayload>(spec.Payload);
        Assert.Equal(60, payload.Strength);
        Assert.Equal(1.2, payload.DurationMult);
        Assert.Equal(0.30, payload.BraindrainOpacity);
        s.BubbleTriggersEnabled = false;
        Assert.Null(BubbleOverlay.RollGlitch(s, new Rolls(0), 0));
        s.BubbleTriggersEnabled = true;
        s.BubbleTriggerVariants = new List<string> { "pink" };
        Assert.Null(BubbleOverlay.RollGlitch(s, new Rolls(0), 0));
    }

    [Fact]
    public async Task PopPaysAmbientRewardAndFiresOnce()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current;
            var (day, paid, firer) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday, EffectPayload.Firer);
            var calls = 0;
            try
            {
                s.AmbientBubbleXpDayKey = "";
                EffectPayload.Firer = p => { Assert.IsType<OverlayPayload>(p); calls++; };
                var spec = BubbleOverlay.RollGlitch(new AppSettings { BubbleTriggersEnabled = true,
                    BubbleTriggerChance = 50, BubbleTriggerVariants = new List<string> { "glitch" } }, new Rolls(0), 0)!;
                var b = new AmbientBubble { AmbientTrigger = true, Spec = spec };
                BubbleOverlay.Pop(b);
                BubbleOverlay.Pop(b);
                Assert.True(b.Popping);
                Assert.Equal(5, AmbientBubbleXp.PaidToday(s));
                Assert.Equal(1, calls);
            }
            finally { s.AmbientBubbleXpDayKey = day; s.AmbientBubbleXpPaidToday = paid; EffectPayload.Firer = firer; }
            return Task.CompletedTask;
        });
    }
}
