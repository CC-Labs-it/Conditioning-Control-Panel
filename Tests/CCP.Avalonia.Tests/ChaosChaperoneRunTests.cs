using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosChaperoneRunTests
{
    private sealed class ZeroRandom : Random { public override double NextDouble() => 0; }

    [Fact]
    public Task Spawn_tick_links_pair_and_routes_seventh_shimmer_to_field_fx() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldRng = ChaosRunHost.Rng;
        var oldNow = ChaosFieldFxOverlay.NowMs;
        var oldSkia = CoreSettings.Current.ChaosSkiaFxEnabled;
        var oldSkip = ChaosFieldFxOverlay.SkipPlatformChecksForTests;
        int oldRuns = ChaosMeta.State.RunsCompleted;
        bool oldSeen = ChaosMeta.State.SeenChaperone;
        var oldDiscovered = new HashSet<string>(ChaosMeta.State.DiscoveredCodexIds);
        try
        {
            ChaosMeta.State.RunsCompleted = 0;
            ChaosMeta.State.SeenChaperone = false;
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
            ChaosFieldFxOverlay.NowMs = () => 0;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.Rng = new ZeroRandom();
            ChaosRunHost.SpawnTick();
            Assert.DoesNotContain(BubbleOverlay.Field.Bubbles, b => b.Spec?.IsChaperoneLive == true);
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosMeta.State.RunsCompleted = 3;
            ChaosRunHost.SpawnTick();
            var live = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsChaperoneLive == true));
            var escort = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsEscort == true));
            Assert.Same(escort, live.Escort);
            Assert.Same(live, escort.OrbitTarget);
            Assert.True(ChaosMeta.State.SeenChaperone);
            Assert.True(ChaosMeta.IsDiscovered("bubble:chaperone"));
            BubbleOverlay.Press(live, live.CenterX, live.CenterY);
            Assert.False(live.Popping);
            Assert.Equal(320, live.ShieldFlashMs);
            var frame = new List<FieldFxPrim>();
            for (int i = 0; i < 6; i++) BubbleOverlay.Field.Step();
            ChaosFieldFxOverlay.Active!.Model.Frame(0, frame);
            Assert.DoesNotContain(frame, p => p.Kind == FieldFxKind.TrailDot);
            BubbleOverlay.Field.Step();
            ChaosFieldFxOverlay.Active!.Model.Frame(0, frame);
            Assert.Single(frame.Where(p => p.Kind == FieldFxKind.TrailDot));
            live.Popping = true;
            BubbleOverlay.Field.Step();
            Assert.True(escort.Popping);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.Rng = oldRng;
            ChaosFieldFxOverlay.NowMs = oldNow;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = oldSkip;
            CoreSettings.Current.ChaosSkiaFxEnabled = oldSkia;
            ChaosMeta.State.RunsCompleted = oldRuns;
            ChaosMeta.State.SeenChaperone = oldSeen;
            ChaosMeta.State.DiscoveredCodexIds.Clear();
            ChaosMeta.State.DiscoveredCodexIds.UnionWith(oldDiscovered);
        }
        return Task.CompletedTask;
    });
}
