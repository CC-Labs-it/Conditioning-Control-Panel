using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosSlowMotionTests
{
    [Fact]
    public Task Pendulum_expires_and_restores_with_freeze_and_shutdown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        bool checks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, PendulumSwing = true });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.State!.PendulumPayMult = 3;
            for (int i = 0; i < 65 && ChaosRunHost.SlowRemainingSec <= 0; i++) ChaosRunHost.Tick();
            Assert.True(ChaosRunHost.SlowRemainingSec > 0);
            ChaosRunHost.ActivatePendulum();
            var spec = new EffectBubbleSpec { Payload = new FlashPayload { Strength = 50 }, PayMult = 1 };
            var before = ChaosRunHost.State.Score;
            ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = spec });
            double expected = ChaosFieldMath.TreatPoints(50, ChaosRunHost.State.BenignBaseline, 1, 3, 1,
                ChaosRunHost.State.TotalMult, ChaosRunHost.State.BlindfoldPayMult);
            Assert.Equal(expected, ChaosRunHost.State.Score - before, 6);
            Assert.Equal(0.12, BubbleOverlay.Field.ChaosTimeScale);
            Assert.Equal(3, ChaosRunHost.PendulumMultiplier);
            Assert.Equal("pendulum", Assert.Single(ChaosEffectBannerOverlay.Active!.Strip.Row, e => e.Id == "slowmo").ArtKey);
            ChaosRunHost.ActivateFreeze();
            for (int i = 0; i < 10; i++) ChaosRunHost.Tick();
            Assert.Equal(0, ChaosRunHost.SlowRemainingSec);
            Assert.Equal(1, ChaosRunHost.PendulumMultiplier);
            Assert.True(BubbleOverlay.Field.FreezeChaos);
            Assert.Equal(2.5, EffectPayload.GlobalDurationMult);
            for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
            Assert.Equal(1, EffectPayload.GlobalDurationMult);
            ChaosRunHost.ActivatePendulum();
            ChaosRunHost.ForceShutdown();
            Assert.Equal(1, BubbleOverlay.Field.ChaosTimeScale);
            Assert.Equal(1, EffectPayload.GlobalDurationMult);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = checks;
        }
        return Task.CompletedTask;
    });
}
