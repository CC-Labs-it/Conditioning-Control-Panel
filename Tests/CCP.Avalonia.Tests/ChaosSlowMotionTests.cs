using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosSlowMotionTests
{
    [Fact]
    public Task Empty_field_refills_during_slow_motion_and_retimes_after_expiry() => WithSpawnRun(() =>
    {
        ChaosRunHost.SpawnTick();
        ChaosRunHost.ActivatePendulum();
        BubbleOverlay.PopChaos(Assert.Single(BubbleOverlay.Field.Bubbles));
        ChaosRunHost.Tick(); // a popping bubble still occupies the WPF field
        Assert.Equal(0, BubbleOverlay.ChaosCount);
        for (int i = 0; i < 20; i++) BubbleOverlay.Field.Step();
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        ChaosRunHost.Tick();
        Assert.Equal(1, BubbleOverlay.ChaosCount);
        var state = ChaosRunHost.State!;
        var slowInterval = TimeSpan.FromMilliseconds(ChaosFieldMath.SpawnIntervalMs(
            state.RunIntensity, state.Config.DifficultyMult, state.Config.SpawnRateMult, 0.12));
        Assert.Equal(slowInterval, ChaosRunHost.SpawnIntervalForTests);
        ChaosRunHost.Tick();
        Assert.Equal(1, BubbleOverlay.ChaosCount); // occupied field is not rescued
        for (int i = 0; i < 7; i++) ChaosRunHost.Tick();
        Assert.Equal(0, ChaosRunHost.SlowRemainingSec);
        BubbleOverlay.PopChaos(Assert.Single(BubbleOverlay.Field.Bubbles));
        ChaosRunHost.Tick(); // the pop animation still occupies the WPF field
        Assert.Equal(0, BubbleOverlay.ChaosCount);
        for (int i = 0; i < 20; i++) BubbleOverlay.Field.Step();
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        ChaosRunHost.Tick();
        Assert.Equal(1, BubbleOverlay.ChaosCount);
        var normalInterval = TimeSpan.FromMilliseconds(ChaosFieldMath.SpawnIntervalMs(
            state.RunIntensity, state.Config.DifficultyMult, state.Config.SpawnRateMult));
        Assert.Equal(normalInterval, ChaosRunHost.SpawnIntervalForTests);
        Assert.True(normalInterval < slowInterval);
    });

    [Fact]
    public Task Empty_field_rescue_waits_during_pause_and_freeze() => WithSpawnRun(() =>
    {
        ChaosRunHost.ToggleManualPause();
        ChaosRunHost.Tick();
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        ChaosRunHost.ToggleManualPause();
        ChaosRunHost.ActivateFreeze();
        for (int i = 0; i < 13; i++) ChaosRunHost.Tick();
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        Assert.True(ChaosRunHost.FreezeRemainingSec > 0);
        ChaosRunHost.Tick(); // expiry and rescue share this run tick in WPF
        Assert.Equal(0, ChaosRunHost.FreezeRemainingSec);
        Assert.Equal(1, BubbleOverlay.ChaosCount);
    });

    private static Task WithSpawnRun(Action test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        bool checks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        try
        {
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                EnabledVariants = new List<string> { "flash" } });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.State!.PrismChance = 0;
            BubbleOverlay.Field.Bubbles.Clear();
            test();
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = checks;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Swing_feedback_matches_scoring_window(bool scoring) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var loc = LocalizationManager.Instance;
        string language = loc.CurrentLanguage;
        bool announcer = CoreSettings.Current.ChaosAnnouncerEnabled;
        bool checks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        var play = ChaosRunHost.PlaySlowCue;
        var cues = new List<(string, float)>();
        try
        {
            loc.SetLanguage("en");
            CoreSettings.Current.ChaosAnnouncerEnabled = true;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.PlaySlowCue = (key, volume) => cues.Add((key, volume));
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, PendulumSwing = true });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.State!.PendulumPayMult = scoring ? 3 : 1;
            for (int i = 0; i < 65 && ChaosRunHost.SlowRemainingSec <= 0; i++) ChaosRunHost.Tick();
            Assert.True(ChaosRunHost.SlowRemainingSec > 0);
            Assert.Equal(new[] { ("time_slow_in", 0.5f), ("ticktock", 0.45f) }, cues);
            var line = ChaosAnnouncerOverlay.Active!.Current!;
            Assert.Equal(scoring ? "focus_here" : "pendulum", line.ArtKey);
            Assert.Equal(scoring ? "🕰 FOCUS HERE. everything pays x3" : "🕰 the pendulum swings", line.Text);
            Assert.Equal(scoring ? "everything pays x3" : null, line.SubText);
            Assert.Equal("🕰 the pendulum swings", ChaosRunHost.State.RecentEvents[0]);
            ChaosRunHost.ActivatePendulum(); // refresh does not replay the in cue
            Assert.Equal(2, cues.Count);
            for (int i = 0; i < 10; i++) ChaosRunHost.Tick();
            Assert.Equal(("time_slow_out", 0.45f), cues.Last());
            ChaosRunHost.ActivatePendulum();
            Assert.Equal(("time_slow_in", 0.5f), cues.Last());
            ChaosRunHost.ForceShutdown();
            Assert.Equal(("time_slow_out", 0.45f), cues.Last());
            Assert.Equal(5, cues.Count);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosAnnouncerOverlay.CloseActive();
            ChaosRunHost.PlaySlowCue = play;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = checks;
            CoreSettings.Current.ChaosAnnouncerEnabled = announcer;
            loc.SetLanguage(language);
        }
        return Task.CompletedTask;
    });

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
