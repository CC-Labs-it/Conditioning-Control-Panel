using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public sealed class ChaosEStimChargeTests
{
    private static AmbientBubble Bubble(double x, bool live = false) => new()
    {
        X = x, Size = 20, Clickable = true,
        Spec = new EffectBubbleSpec { IsLive = live, FuseMs = 10000 },
        FuseTotalMs = 10000, FuseRemainingMs = 10000
    };

    [Fact]
    public void Equipped_toy_charges_clicks_snaps_live_bubbles_and_cancels_old_hops() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var meta = ChaosMeta.State;
        var active = meta.ActiveLifetimeBoons.ToArray();
        var levels = meta.LifetimeBoonLevels.ToArray();
        int pockets = meta.ToyPockets;
        var settings = CoreSettings.Current;
        bool skia = settings.ChaosSkiaFxEnabled;
        var schedule = BubbleOverlay.ScheduleEStimHit;
        var canShow = ChaosEStimOverlay.CanShow;
        var read = ChaosEStimGlow.ReadPointer;
        var firer = EffectPayload.Firer;
        var audio = CoreAudio.PlayOneShotProvider;
        var pending = new List<(int Delay, Action Run)>();
        var cues = new List<string>();
        meta.ActiveLifetimeBoons.Clear();
        meta.ActiveLifetimeBoons.Add("e_stim");
        meta.LifetimeBoonLevels["e_stim"] = 1;
        meta.ToyPockets = 1;
        settings.ChaosSkiaFxEnabled = false;
        BubbleOverlay.ScheduleEStimHit = (delay, action) => pending.Add((delay, action));
        ChaosEStimOverlay.CanShow = () => true;
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimGlow.ReadPointer = () => (new PixelPoint(500, 300), false, false);
        EffectPayload.Firer = _ => { };
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(Path.GetFileName(path));
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!;
            var toy = Assert.Single(state.ActiveToys);
            Assert.Equal("e_stim", toy.Id);
            state.ToyPower["e_stim"] = 1;
            state.EStimChargeMult = 2;
            ChaosRunHost.UseToyById("e_stim");
            Assert.Equal(2, BubbleOverlay.Field.EStimChargesLeft);
            Assert.Equal(30, toy.CooldownRemainingSec);
            Assert.True(toy.IsEffectActive);
            Assert.True(ChaosEStimGlow.Active!.IsVisible);
            Assert.True(ChaosEffectBannerOverlay.Active!.Strip.IsLive("estim"));
            Assert.Contains("toy_ready.mp3", cues);

            // Empty space holds the charge; a direct sweep pop is not a player click.
            var field = BubbleOverlay.Field;
            field.Bubbles.Clear();
            var lone = Bubble(0);
            field.Bubbles.Add(lone);
            BubbleOverlay.Press(lone, lone.CenterX, lone.CenterY);
            Assert.Equal(2, field.EStimChargesLeft);
            ChaosRunHost.Tick();
            Assert.True(toy.IsEffectActive);
            var swept = Bubble(10);
            field.Bubbles.AddRange(new[] { swept, Bubble(80) });
            BubbleOverlay.PopChaos(swept);
            Assert.Equal(2, field.EStimChargesLeft);
            Assert.Empty(pending);

            field.Bubbles.Clear();
            var source = Bubble(0);
            var live = Bubble(100, live: true);
            var treat = Bubble(200);
            field.Bubbles.AddRange(new[] { source, live, treat });
            ChaosRunHost.ToggleManualPause();
            BubbleOverlay.Press(source, source.CenterX, source.CenterY);
            Assert.False(source.Popping);
            Assert.Equal(2, field.EStimChargesLeft);
            ChaosRunHost.ToggleManualPause();
            BubbleOverlay.Press(source, source.CenterX, source.CenterY);
            Assert.Equal(1, field.EStimChargesLeft);
            Assert.Equal(new[] { 70, 140 }, pending.Select(x => x.Delay));
            Assert.True(ChaosEStimGlow.Active!.IsVisible);
            Assert.Contains("estim_zap.mp3", cues);
            Assert.False(live.Popping || treat.Popping);
            var (focus, defused) = (state.Focus, state.Defused);
            pending[0].Run();
            Assert.True(live.Deflating);
            Assert.Equal(focus, state.Focus);
            Assert.Equal(defused + 1, state.Defused);
            pending[1].Run();
            Assert.True(treat.Popping);

            field.Bubbles.Clear();
            var last = Bubble(0);
            var target = Bubble(100);
            field.Bubbles.AddRange(new[] { last, target });
            BubbleOverlay.Press(last, last.CenterX, last.CenterY);
            Assert.Equal(0, field.EStimChargesLeft);
            Assert.False(ChaosEStimGlow.Active!.IsVisible);
            Assert.False(ChaosEffectBannerOverlay.Active!.Strip.IsLive("estim"));
            ChaosRunHost.Tick();
            Assert.False(toy.IsEffectActive);
            var staleHop = pending[2].Run;
            ChaosRunHost.ForceShutdown();
            Assert.Equal(0, field.EStimChargesLeft);
            Assert.Null(ChaosEStimGlow.Active);

            // Even if the old target is present in a replacement run, a late timer is inert.
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            field.Bubbles.Add(target);
            staleHop();
            Assert.False(target.Popping);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.ScheduleEStimHit = schedule;
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = false;
            ChaosEStimGlow.ReadPointer = read;
            EffectPayload.Firer = firer;
            CoreAudio.PlayOneShotProvider = audio;
            settings.ChaosSkiaFxEnabled = skia;
            meta.ToyPockets = pockets;
            meta.ActiveLifetimeBoons.Clear();
            foreach (var id in active) meta.ActiveLifetimeBoons.Add(id);
            meta.LifetimeBoonLevels.Clear();
            foreach (var entry in levels) meta.LifetimeBoonLevels[entry.Key] = entry.Value;
        }
    });
}
