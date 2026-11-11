using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosChaperoneTests
{
    private sealed class AlwaysHit : Random { public override double NextDouble() => 0; }

    [Fact]
    public Task Spawned_escort_shields_live_and_shimmers_every_seventh_step() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        bool enabled = settings.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var rng = ChaosRunHost.Rng;
        int runs = ChaosMeta.State.RunsCompleted;
        bool seen = ChaosMeta.State.SeenChaperone;
        try
        {
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosMeta.State.RunsCompleted = 100;
            ChaosMeta.State.SeenChaperone = false;
            ChaosRunHost.Rng = new AlwaysHit();
            ChaosRunHost.SpawnTick();
            var live = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsChaperoneLive == true));
            Assert.True(ChaosMeta.State.SeenChaperone);
            var escort = live.Escort!;
            Assert.NotNull(escort);
            BubbleOverlay.Press(live, live.CenterX, live.CenterY);
            Assert.False(live.Channeling);
            BubbleOverlay.PopChaos(live);
            Assert.False(live.Popping);
            BubbleOverlay.PopChaosInRect(new Rect(live.CenterX - 1, live.CenterY - 1, 2, 2));
            Assert.False(live.Popping);
            ChaosSkiaFxOverlay.CloseActive();
            for (int i = 0; i < 6; i++) BubbleOverlay.StepChaosField();
            Assert.Null(ChaosSkiaFxOverlay.Active);
            BubbleOverlay.StepChaosField();
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
            Assert.True(ChaosSkiaFxOverlay.Active!.Fx.ParticleCount > 0);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosSkiaFxOverlay.CloseActive();
            ChaosRunHost.Rng = rng;
            ChaosMeta.State.RunsCompleted = runs;
            ChaosMeta.State.SeenChaperone = seen;
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            settings.ChaosSkiaFxEnabled = enabled;
        }
        return Task.CompletedTask;
    });
}
