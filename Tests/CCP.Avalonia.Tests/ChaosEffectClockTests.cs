using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosEffectClockTests
{
    [Fact]
    public Task Freeze_refresh_pause_expiry_and_shutdown_are_run_scoped() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var meta = ChaosMeta.State;
        int sparks = meta.Sparks;
        bool hadAward = meta.FirstTimesAwarded.Contains(ChaosFirstTimes.Play);
        bool checks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
        try
        {
            void Start()
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
            }
            void Ticks(int n) { for (int i = 0; i < n; i++) ChaosRunHost.Tick(); }
            Start();
            var toy = new ChaosToyState { Id = "freeze_trigger", ChargesLeft = 3 };
            ChaosRunHost.State!.ActiveToys.Add(toy);
            ChaosRunHost.UseToyById("freeze_trigger");
            Assert.Equal(3.5, ChaosRunHost.FreezeRemainingSec);
            Assert.True(BubbleOverlay.Field.FreezeChaos);
            Assert.Equal(2.5, EffectPayload.GlobalDurationMult);
            Assert.Contains(ChaosEffectBannerOverlay.Active!.Strip.Row, e => e.Id == "freeze");
            Ticks(12);
            ChaosRunHost.UseToyById("freeze_trigger");
            Assert.Equal(3.5, ChaosRunHost.FreezeRemainingSec);
            ChaosRunHost.ToggleManualPause();
            Ticks(20);
            Assert.Equal(3.5, ChaosRunHost.FreezeRemainingSec);
            ChaosRunHost.ToggleManualPause();
            Ticks(14);
            Assert.Equal(0, ChaosRunHost.FreezeRemainingSec);
            Assert.False(BubbleOverlay.Field.FreezeChaos);
            Assert.Equal(1, EffectPayload.GlobalDurationMult);
            Assert.False(Assert.Single(ChaosEffectBannerOverlay.Active!.Strip.Row, e => e.Id == "freeze").IsLive);
            toy.CooldownRemainingSec = 0;
            ChaosRunHost.UseToyById("freeze_trigger");
            Assert.True(BubbleOverlay.Field.FreezeChaos);
            ChaosRunHost.ForceShutdown();
            Assert.False(BubbleOverlay.Field.FreezeChaos);
            Assert.Equal(1, EffectPayload.GlobalDurationMult);
            Assert.Null(ChaosEffectBannerOverlay.Active);
            Start();
            Assert.Equal(0, ChaosRunHost.FreezeRemainingSec);
            Assert.False(BubbleOverlay.Field.FreezeChaos);
            Assert.Empty(ChaosEffectBannerOverlay.Active!.Strip.Row);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = checks;
            meta.Sparks = sparks;
            if (hadAward) meta.FirstTimesAwarded.Add(ChaosFirstTimes.Play);
            else meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        }
        return Task.CompletedTask;
    });
}
