using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public async Task Golden_chance_adds_a_bonus_bubble()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var oldRng = ChaosRunHost.Rng;
            var oldAudio = CoreAudio.PlayOneShotProvider;
            var sounds = new List<string>();
            try
            {
                Start();
                CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => sounds.Add(System.IO.Path.GetFileName(path));
                ChaosRunHost.State!.GoldenChance = 1;
                ChaosRunHost.SpawnTick();
                Assert.Contains(BubbleOverlay.Field.Bubbles, b => b.Spec?.IsGolden == true);
                Assert.Contains(BubbleOverlay.Field.Bubbles, b => b.Spec != null && !b.Spec.IsGolden);
                Assert.Contains("chime1.mp3", sounds);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.Rng = oldRng; CoreAudio.PlayOneShotProvider = oldAudio; }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Golden_and_droplet_pop_bank_float_and_skip_treat(bool golden, bool digger)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var oldRng = ChaosRunHost.Rng;
            var oldText = ChaosRunHost.PopText;
            var oldFirer = EffectPayload.Firer;
            var oldPulse = ChaosRunHost.PulseFx;
            var oldAudio = CoreAudio.PlayOneShotProvider;
            var sounds = new List<string>();
            bool oldAnnouncer = CoreSettings.Current.ChaosAnnouncerEnabled;
            var texts = new List<(double X, double Y, string Word, Color Color)>();
            var pulses = new List<double>();
            try
            {
                Start();
                ChaosAnnouncerOverlay.CloseActive();
                CoreSettings.Current.ChaosAnnouncerEnabled = true;
                ChaosRunHost.PopText = (x, y, w, c) => texts.Add((x, y, w, c));
                ChaosRunHost.PulseFx = (_, strength) => pulses.Add(strength);
                CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => sounds.Add(System.IO.Path.GetFileName(path));
                int fires = 0;
                EffectPayload.Firer = _ => fires++;
                var state = ChaosRunHost.State!;
                state.GoldDiggerEnabled = digger;
                state.Focus = 20;
                int before = ChaosMeta.State.Gold;
                double score = state.Score;
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsGolden = golden, IsDroplet = !golden, SizePx = 100, Payload = new FlashPayload() });
                BubbleOverlay.PopChaos(b);
                int paid = ChaosMeta.State.Gold - before;
                Assert.Equal(golden ? 10 : 3, paid);
                Assert.Equal(20 + (golden ? ChaosTuning.FOCUS_PER_GOLDEN : ChaosTuning.FOCUS_PER_DROPLET), state.Focus);
                Assert.Contains(texts, t => t.X == b.CenterX && t.Y == b.CenterY + 30 && t.Word == $"+{paid} {ChaosGlyphs.Gold}" && t.Color == Color.FromRgb(255, 215, 0));
                Assert.Contains(pulses, p => p == (golden ? 0.35 : 0.12));
                Assert.Contains("golden_pop.mp3", sounds);
                Assert.Equal(score, state.Score);
                Assert.Equal(0, fires);
                Assert.Contains(state.RecentEvents, e => e.Contains(golden ? "lucky bubble" : "droplet"));
                var droplets = BubbleOverlay.Field.Bubbles.Where(x => x.Spec?.IsDroplet == true && x != b).ToArray();
                Assert.Equal(golden && digger ? 3 : 0, droplets.Length);
                if (golden && digger) Assert.Contains(state.RecentEvents, e => e.Contains("gold digger spills"));
                foreach (var d in droplets) { Assert.InRange(d.CenterX, b.CenterX - 50, b.CenterX + 50); Assert.InRange(d.CenterY, b.CenterY - 20, b.CenterY + 20); }
                if (golden) Assert.Contains($"+{paid} gold", ChaosAnnouncerOverlay.Active!.Current!.Text);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.Rng = oldRng; ChaosRunHost.PopText = oldText; EffectPayload.Firer = oldFirer; ChaosRunHost.PulseFx = oldPulse; CoreAudio.PlayOneShotProvider = oldAudio; CoreSettings.Current.ChaosAnnouncerEnabled = oldAnnouncer; }
            return Task.CompletedTask;
        });
    }
}
