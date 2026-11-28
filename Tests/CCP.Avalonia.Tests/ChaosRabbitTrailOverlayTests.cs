using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosRabbitTrailOverlayTests
{
    // WPF BubbleService.cs:3936-3949: a travelling rabbit drops Skia trail dots only after its telegraph.
    [Fact]
    public Task Tail_plug_rabbit_drops_trail_dots_after_telegraph() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        bool enabled = settings.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        try
        {
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.State!.RabbitTrailSec = 2.0;
            var rabbit = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildDarter(0, atPxX: 400, atPxY: 300))!;
            ChaosSkiaFxOverlay.CloseActive();
            for (int i = 0; i < 5; i++) { rabbit.X += 30; BubbleOverlay.StepRabbitTrails(); }
            Assert.Null(ChaosSkiaFxOverlay.Active);   // still telegraphing
            for (int i = 0; i < 20; i++) { rabbit.X += 30; BubbleOverlay.StepRabbitTrails(); }
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
            Assert.True(ChaosSkiaFxOverlay.Active!.Fx.ParticleCount > 0);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            settings.ChaosSkiaFxEnabled = enabled;
        }
        return Task.CompletedTask;
    });
}
