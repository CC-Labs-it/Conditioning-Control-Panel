using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Pendulum slow-mo (hand-ported from #4303) and the Freeze Trigger toy (from #4244), restack b145.
/// WPF ChaosModeService.cs:2406-2475, :2718-2727, :2958-2979.</summary>
public sealed class ChaosSlowMotionTests
{
    private static void Run(Action<ChaosRunState> test, bool pendulum = true) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        bool checks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            ChaosMeta.Init();
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, PendulumSwing = pendulum });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            test(ChaosRunHost.State!);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            shell.Close();
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = checks;
        }
    });

    private static void TickUntilSwing()
    {
        for (int i = 0; i < 80 && ChaosRunHost.SlowMoRemainingSec <= 0; i++) ChaosRunHost.Tick();
    }

    [Fact]
    public void Pendulum_swings_once_a_loop_and_slows_the_field() => Run(s =>
    {
        TickUntilSwing();
        Assert.True(ChaosRunHost.SlowMoRemainingSec > 0);
        Assert.Equal(ChaosRunHost.SlowMoFactor, BubbleOverlay.Field.ChaosTimeScale);
        Assert.Equal(1 / ChaosRunHost.SlowMoFactor, EffectPayload.GlobalDurationMult, 6);
        Assert.Equal(1.0, ChaosRunHost.PendulumMultiplier);   // no Focus Here: a swing, not a scoring window
        Assert.Contains("slowmo", ChaosEffectBannerOverlay.Active!.Strip.Row.Select(e => e.Id));
        for (int i = 0; i < 11; i++) ChaosRunHost.Tick();   // 2.5 s at 0.25 s a tick
        Assert.Equal(0, ChaosRunHost.SlowMoRemainingSec);
        Assert.Equal(1.0, BubbleOverlay.Field.ChaosTimeScale);
        Assert.Equal(1.0, EffectPayload.GlobalDurationMult);
        int wave = s.WaveIndex;
        for (int i = 0; i < 20 && s.WaveIndex == wave; i++) ChaosRunHost.Tick();
        if (s.WaveIndex == wave) Assert.Equal(0, ChaosRunHost.SlowMoRemainingSec);   // fired this loop already
    });

    [Fact]
    public void Focus_here_pays_x3_only_inside_the_swing() => Run(s =>
    {
        s.PendulumPayMult = 3;
        var spec = new EffectBubbleSpec { Payload = new FlashPayload { Strength = 50 }, PayMult = 1 };
        double before = s.Score;
        ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = spec });
        double plain = s.Score - before;
        TickUntilSwing();
        Assert.Equal(3.0, ChaosRunHost.PendulumMultiplier);
        before = s.Score;
        ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = spec });
        Assert.Equal(ChaosFieldMath.TreatPoints(50, s.BenignBaseline, 1, 3, 1, s.TotalMult, s.BlindfoldPayMult),
            s.Score - before, 6);
        Assert.True(s.Score - before > plain);
    });

    [Fact]
    public void Freeze_inside_a_swing_keeps_its_duration_mult_and_shutdown_restores_everything() => Run(s =>
    {
        TickUntilSwing();
        var toy = new ChaosToyState { Id = "freeze_trigger", KeyLabel = "F", ChargesLeft = 1 };
        s.ActiveToys.Clear(); s.ActiveToys.Add(toy);
        ChaosRunHost.UseToyById("freeze_trigger");
        Assert.True(BubbleOverlay.Field.FreezeChaos);
        Assert.Equal(0, toy.ChargesLeft);
        for (int i = 0; i < 11; i++) ChaosRunHost.Tick();   // the swing ends under the freeze
        Assert.Equal(0, ChaosRunHost.SlowMoRemainingSec);
        Assert.Equal(2.5, EffectPayload.GlobalDurationMult);   // EndSlowMo leaves a running freeze alone
        ChaosRunHost.ForceShutdown();
        Assert.False(BubbleOverlay.Field.FreezeChaos);
        Assert.Equal(1.0, BubbleOverlay.Field.ChaosTimeScale);
        Assert.Equal(1.0, EffectPayload.GlobalDurationMult);
    });

    [Fact]
    public void Freeze_trigger_toy_freezes_spends_a_charge_and_lights_while_it_lasts() => Run(s =>
    {
        var toy = new ChaosToyState { Id = "freeze_trigger", KeyLabel = "F", ChargesLeft = 2 };
        s.ActiveToys.Clear(); s.ActiveToys.Add(toy);
        var live = new AmbientBubble { X = 0, Y = 0, Size = 40, Clickable = true,
            Spec = new EffectBubbleSpec { IsLive = true, FuseMs = 5000 }, FuseTotalMs = 5000, FuseRemainingMs = 5000 };
        BubbleOverlay.Field.Bubbles.Add(live);
        ChaosRunHost.OnToyKey("F");
        Assert.True(BubbleOverlay.Field.FreezeChaos);
        Assert.Equal(1, toy.ChargesLeft);
        Assert.Equal(3, toy.CooldownRemainingSec);
        Assert.False(live.Popping);   // not maxed: the capstone sweep stays off
        ChaosRunHost.Tick();
        Assert.True(toy.IsEffectActive);
        Assert.Contains(s.RecentEvents, e => e.EndsWith("everything holds still"));
        s.MaxedBoons.Add("freeze_trigger");
        toy.CooldownRemainingSec = 0;
        ChaosRunHost.OnToyKey("F");
        Assert.True(live.Popping);   // the maxed toy snaps every live bubble
        Assert.Equal(0, toy.ChargesLeft);
    }, pendulum: false);
}
