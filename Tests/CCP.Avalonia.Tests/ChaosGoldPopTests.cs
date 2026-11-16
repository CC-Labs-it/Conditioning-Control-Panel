using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosGoldPopTests
{
    private sealed class FixedRandom : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static void Start()
    {
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false, EnabledVariants = new() { "flash" } });
        ChaosRunHost.Overlay!.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
        BubbleOverlay.Field.Bubbles.Clear();
        ChaosRunHost.Rng = new FixedRandom();
    }

    [Theory]
    [InlineData("treat", true)]
    [InlineData("prism", true)]
    [InlineData("snap", true)]
    [InlineData("treat", false)]
    [InlineData("prism", false)]
    [InlineData("snap", false)]
    public async Task CamGirl_tip_pays_at_the_pop_only_with_the_boon(string kind, bool enabled)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var oldRng = ChaosRunHost.Rng;
            var oldText = ChaosRunHost.PopText;
            var oldFirer = EffectPayload.Firer;
            var texts = new List<(double X, double Y, string Word, Color Color)>();
            try
            {
                Start();
                EffectPayload.Firer = _ => { };
                ChaosRunHost.PopText = (x, y, w, c) => texts.Add((x, y, w, c));
                ChaosRunHost.State!.CamGirlTipChance = enabled ? 1 : 0;
                int before = ChaosMeta.State.Gold;
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsPrism = kind == "prism", IsLive = kind == "snap", FuseMs = 5000, SizePx = 100, Payload = new FlashPayload() });
                BubbleOverlay.ChaosLastPopX = b.CenterX;
                BubbleOverlay.ChaosLastPopY = b.CenterY;
                if (kind == "snap") ChaosRunHost.OnDefused(b); else BubbleOverlay.PopChaos(b);
                int paid = ChaosMeta.State.Gold - before;
                if (enabled)
                {
                    Assert.Equal(2, paid);
                    Assert.Contains(texts, t => t.X == b.CenterX && t.Y == b.CenterY + 30 && t.Word == $"+{paid} {ChaosGlyphs.Gold}" && t.Color == Color.FromRgb(255, 215, 0));
                    Assert.Contains(ChaosRunHost.State.RecentEvents, e => e.Contains("tipped +2 gold"));
                }
                else { Assert.Equal(0, paid); Assert.DoesNotContain(texts, t => t.Word.EndsWith(ChaosGlyphs.Gold)); }
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.Rng = oldRng; ChaosRunHost.PopText = oldText; EffectPayload.Firer = oldFirer; }
            return Task.CompletedTask;
        });
    }
}
