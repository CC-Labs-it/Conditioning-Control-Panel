using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosRabbitSpawnOrderTests
{
    private sealed class BehavioralDice(bool missChaperone) : Random
    {
        private bool _first = true;
        public override double NextDouble()
        {
            if (_first && missChaperone) { _first = false; return 0.99; }
            return 0;
        }
        public override int Next(int minValue, int maxValue) => minValue;
    }

    // WPF ChaosModeService.cs:1114-1120,1202-1213: the slot is filled before the independent rabbit roll.
    [Theory]
    [InlineData("ordinary")]
    [InlineData("chaperone")]
    [InlineData("tease")]
    public void Rabbit_roll_follows_the_slot_even_when_it_fills_the_cap(string slot) => AvaloniaTestDispatcher.Run(() =>
    {
        var meta = ChaosMeta.State;
        int oldRuns = meta.RunsCompleted;
        bool oldChaperone = meta.SeenChaperone, oldTease = meta.SeenTease;
        var oldDiscovered = new HashSet<string>(meta.DiscoveredCodexIds);
        var oldRng = ChaosRunHost.Rng;
        try
        {
            var cfg = new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false,
                DartersEnabled = true, EnabledVariants = new() { "flash" } };
            ChaosRunHost.StartRun(cfg);
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!;
            state.RabbitRateMult = 1000; // the smallest WPF chance becomes greater than one
            state.PrismChance = 0;
            state.HeavyDropEvery = 0;
            meta.RunsCompleted = slot == "ordinary" ? 0 : 100;
            meta.SeenChaperone = meta.SeenTease = true;
            ChaosRunHost.Rng = new BehavioralDice(slot == "tease");
            BubbleOverlay.Field.Bubbles.Clear();
            int cap = ChaosFieldMath.MaxConcurrent(state.RunIntensity, cfg.DifficultyMult);
            if (slot == "ordinary")
                for (int i = 0; i < cap - 1; i++) BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { Spec = new EffectBubbleSpec() });
            int before = BubbleOverlay.Field.Bubbles.Count;

            ChaosRunHost.SpawnTick();
            var spawned = BubbleOverlay.Field.Bubbles.Skip(before).ToArray();
            Assert.Equal(slot == "chaperone" ? 3 : 2, spawned.Length);
            Assert.True(spawned[^1].Spec!.IsDarter);
            Assert.False(spawned[0].Spec!.IsDarter);
            if (slot == "ordinary") Assert.Equal("flash", spawned[0].Spec!.VariantId);
            if (slot == "chaperone") Assert.True(spawned[0].Spec!.IsChaperoneLive && spawned[1].Spec!.IsEscort);
            if (slot == "tease") Assert.True(spawned[0].Spec!.IsTease);
            var timer = Assert.IsType<DispatcherTimer>(typeof(ChaosRunHost).GetField("_spawnTimer", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
            Assert.Equal(TimeSpan.FromMilliseconds(ChaosFieldMath.SpawnIntervalMs(state.RunIntensity, cfg.DifficultyMult, cfg.SpawnRateMult, 1)), timer.Interval);

            if (slot == "ordinary")
            {
                BubbleOverlay.Field.Bubbles.Remove(spawned[^1]); // field now exactly at the cap
                Assert.Equal(cap, BubbleOverlay.ChaosCount);
                before = BubbleOverlay.Field.Bubbles.Count;
                ChaosRunHost.SpawnTick();
                var extra = Assert.Single(BubbleOverlay.Field.Bubbles.Skip(before));
                Assert.True(extra.Spec!.IsDarter);
                cfg.DartersEnabled = false;
                before = BubbleOverlay.Field.Bubbles.Count;
                ChaosRunHost.SpawnTick();
                Assert.Equal(before, BubbleOverlay.Field.Bubbles.Count);
            }
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.Rng = oldRng;
            meta.RunsCompleted = oldRuns;
            meta.SeenChaperone = oldChaperone;
            meta.SeenTease = oldTease;
            meta.DiscoveredCodexIds.Clear();
            meta.DiscoveredCodexIds.UnionWith(oldDiscovered);
        }
    });
}
