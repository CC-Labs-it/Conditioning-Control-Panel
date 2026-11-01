using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ChaosModeService.cs:2765-2803 and BubbleService.cs:351-442.</summary>
public class ChaosEStimToyTests
{
    private static AmbientBubble Bubble(double x, bool live = false)
    {
        var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { VariantId = "flash", IsLive = live, FuseMs = 9000, SizePx = 40 });
        b.X = x; b.Y = 100;
        return b;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Equipped_toy_arms_clicks_pays_hops_and_cleans_up(bool maxed, bool skia)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            for (int i = 1; i <= 3; i++) ChaosMetaStore.Delete(i);
            ChaosMeta.Init();
            var meta = ChaosMeta.State;
            meta.ToyPockets = 1;
            meta.ActiveLifetimeBoons.Add("e_stim");
            meta.LifetimeBoonLevels["e_stim"] = 1;
            var settings = CoreSettings.Current;
            var oldSkiaEnabled = settings.ChaosSkiaFxEnabled;
            var oldComposited = ChaosSkiaFxOverlay.Composited;
            var oldSkiaSkip = ChaosSkiaFxOverlay.SkipPlatformChecksForTests;
            settings.ChaosSkiaFxEnabled = skia;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            var oldCanShow = ChaosEStimOverlay.CanShow;
            var oldPointer = ChaosEStimGlow.ReadPointer;
            var oldAudio = CoreAudio.PlayOneShotProvider;
            var oldPulse = ChaosRunHost.PulseFx;
            var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
            LocalizationManager.Instance.SetLanguage("en");
            var cues = new System.Collections.Generic.List<string>();
            var pulses = new System.Collections.Generic.List<string>();
            ChaosEStimOverlay.CanShow = () => true;
            ChaosEStimOverlay.SkipPlatformChecksForTests = true;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            ChaosEStimGlow.ReadPointer = () => (new PixelPoint(100, 100), false, false);
            CoreAudio.PlayOneShotProvider = (p, _, _, _, _) => cues.Add(System.IO.Path.GetFileNameWithoutExtension(p));
            ChaosRunHost.PulseFx = (c, strength) => pulses.Add($"{c.R},{c.G},{c.B}@{strength:0.00}");
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                state.EStimChargeMult = 2;
                if (maxed) state.MaxedBoons.Add("e_stim");
                var toy = Assert.Single(ChaosRunHost.ToyButtons).Toy;
                int sparks = meta.Sparks;
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById("e_stim");
                Assert.Equal(0, BubbleOverlay.EStimChargesLeft);
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById("e_stim");
                Assert.Equal(6, BubbleOverlay.EStimChargesLeft);
                Assert.Equal(30, toy.CooldownRemainingSec);
                Assert.True(ChaosEStimGlow.Active!.IsVisible);
                Assert.True(ChaosEffectBannerOverlay.Active!.Strip.IsLive("estim"));
                Assert.Contains("toy_ready", cues);
                Assert.Contains("156,92,255@0.25", pulses);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Play));
                Assert.Equal(sparks + 15, meta.Sparks);
                ChaosRunHost.Tick();
                Assert.True(toy.IsEffectActive);

                foreach (var spec in new[] { new EffectBubbleSpec { IsTease = true, SizePx = 40 },
                                             new EffectBubbleSpec { IsBrittle = true, SizePx = 40 } })
                {
                    var immune = BubbleOverlay.SpawnChaos(spec);
                    immune.X = 0; immune.Y = 100;
                    var guardTarget = Bubble(100);
                    BubbleOverlay.Press(immune, immune.CenterX, immune.CenterY);
                    Assert.Equal(6, BubbleOverlay.EStimChargesLeft);
                    BubbleOverlay.PopChaos(guardTarget);
                }
                var lonely = Bubble(2000);
                BubbleOverlay.Press(lonely, lonely.CenterX, lonely.CenterY);
                Assert.Equal(6, BubbleOverlay.EStimChargesLeft); // no conductor, charge waits
                var swept = Bubble(10);
                BubbleOverlay.PopChaos(swept); // a skill pop never discharges
                Assert.Equal(6, BubbleOverlay.EStimChargesLeft);
                var source = Bubble(0); var treat = Bubble(100); var live = Bubble(200, true);
                var secondHop = Bubble(750);
                BubbleOverlay.Press(source, source.CenterX, source.CenterY);
                Assert.Equal(5, BubbleOverlay.EStimChargesLeft);
                Assert.Contains("5", state.RecentEvents.First());
                Assert.Equal("⚡ current arcs. 5 left", state.RecentEvents.First());
                if (skia)
                {
                    Assert.NotNull(ChaosSkiaFxOverlay.Active);
                    Assert.True(ChaosSkiaFxOverlay.Active!.Fx.BoltCount > 0);
                    Assert.Null(ChaosEStimOverlay.Active);
                }
                else
                {
                    Assert.Null(ChaosSkiaFxOverlay.Active);
                    Assert.NotNull(ChaosEStimOverlay.Active);
                    Assert.True(ChaosEStimOverlay.Active!.BoltCount > 0);
                }
                Assert.Contains("estim_zap", cues);
                Assert.False(treat.Popping); // first hop waits 70 ms
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while ((!treat.Popping || !live.Popping || (maxed && !secondHop.Popping))
                       && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
                Assert.True(treat.Popping);
                Assert.True(live.Popping);
                Assert.Equal(maxed, secondHop.Popping);
                Assert.Equal(1, state.Defused);

                BubbleOverlay.ArmEStim(2, false);
                var pauseSource = Bubble(0); var pausedTarget = Bubble(100); var pausedLive = Bubble(180, true);
                ChaosRunHost.ToggleManualPause();
                BubbleOverlay.Press(pauseSource, pauseSource.CenterX, pauseSource.CenterY);
                Assert.False(pauseSource.Popping);
                Assert.Equal(2, BubbleOverlay.EStimChargesLeft);
                ChaosRunHost.ToggleManualPause();
                LocalizationManager.Instance.SetLanguage("es");
                BubbleOverlay.Press(pauseSource, pauseSource.CenterX, pauseSource.CenterY);
                Assert.Equal("⚡ la corriente salta. quedan 1", state.RecentEvents.First());
                var feed = IconText.Split(state.RecentEvents.First());
                Assert.Equal(new[] { "⚡" }, feed.Icons);
                Assert.Equal("la corriente salta. quedan 1", feed.Rest);
                ChaosRunHost.ToggleManualPause();
                await Task.Delay(350); // WPF drops a hop that lands while the field is locked
                Assert.False(pausedTarget.Popping);
                Assert.False(pausedLive.Popping);
                ChaosRunHost.ToggleManualPause();

                BubbleOverlay.ArmEStim(1, false);
                var next = Bubble(0); var pending = Bubble(100);
                BubbleOverlay.Press(next, next.CenterX, next.CenterY);
                Assert.Equal(0, BubbleOverlay.EStimChargesLeft);
                Assert.Equal("⚡ carga agotada", state.RecentEvents.First());
                Assert.False(ChaosEStimGlow.Active!.IsVisible);
                Assert.False(ChaosEffectBannerOverlay.Active!.Strip.IsLive("estim"));
                ChaosRunHost.Tick();
                Assert.False(toy.IsEffectActive);
                ChaosRunHost.ForceShutdown();
                await Task.Delay(350);
                Assert.False(pending.Popping); // an old hop cannot hit a later run
                Assert.Equal(0, BubbleOverlay.EStimChargesLeft);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                LocalizationManager.Instance.SetLanguage(oldLanguage);
                ChaosSkiaFxOverlay.CloseActive();
                settings.ChaosSkiaFxEnabled = oldSkiaEnabled;
                ChaosSkiaFxOverlay.Composited = oldComposited;
                ChaosSkiaFxOverlay.SkipPlatformChecksForTests = oldSkiaSkip;
                ChaosRunHost.PulseFx = oldPulse;
                CoreAudio.PlayOneShotProvider = oldAudio;
                ChaosEStimOverlay.CanShow = oldCanShow;
                ChaosEStimGlow.ReadPointer = oldPointer;
                ChaosEStimOverlay.SkipPlatformChecksForTests = false;
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = false;
                for (int i = 1; i <= 3; i++) ChaosMetaStore.Delete(i);
                ChaosMeta.Init();
            }
        });
    }
}
