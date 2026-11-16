using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Rabbit Caller (WPF ChaosModeService.cs:2751-2759, :2843-2911, :2986-2993): the whistle arms
/// the halo and spends the cooldown, the arming press is ignored, the next fresh click summons the
/// rabbits within 60 px of the cursor, pauses hold the whistle, the capstone storm adds rabbits, and
/// teardown and panic stand it down.</summary>
public sealed class RabbitCallerDriverTests
{
    private static Task Run(Action<ChaosToyState, Action<int, int, bool>, List<string>> body, bool maxed = false) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            AmbientBeatLoopsTests.Setup();
            var s = CoreSettings.Current;
            var saved = (s.ChaosSkiaFxEnabled, ChaosRunHost.ReadToyPointer, ChaosCursorGlowOverlay.ReadPointer,
                ChaosCursorGlowOverlay.MakeClickThrough, ChaosEffectBannerOverlay.SkipPlatformChecksForTests,
                ConditioningControlPanel.CoreAudio.PlayOneShotProvider, LocalizationManager.Instance.CurrentLanguage);
            var cues = new List<string>();
            var at = new PixelPoint(0, 0);
            bool down = false;
            try
            {
                LocalizationManager.Instance.SetLanguage("en");
                s.ChaosSkiaFxEnabled = false;   // the classic halo window path
                ChaosRunHost.ReadToyPointer = () => (at, down, false);
                ChaosCursorGlowOverlay.ReadPointer = () => (at, false, false);
                ChaosCursorGlowOverlay.MakeClickThrough = _ => true;
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
                ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                BubbleOverlay.Field.Bubbles.Clear();
                var state = ChaosRunHost.State!;
                state.ActiveToys.Clear();
                state.ToyPower["rabbit_caller"] = 3;
                if (maxed) state.MaxedBoons.Add("rabbit_caller"); else state.MaxedBoons.Remove("rabbit_caller");
                var toy = new ChaosToyState { Id = "rabbit_caller", KeyLabel = "Q", CooldownSec = 20, Glyph = "R" };
                state.ActiveToys.Add(toy);
                body(toy, (x, y, d) => { at = new PixelPoint(x, y); down = d; }, cues);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                BubbleOverlay.Field.Bubbles.Clear();
                (s.ChaosSkiaFxEnabled, ChaosRunHost.ReadToyPointer, ChaosCursorGlowOverlay.ReadPointer,
                    ChaosCursorGlowOverlay.MakeClickThrough, ChaosEffectBannerOverlay.SkipPlatformChecksForTests,
                    ConditioningControlPanel.CoreAudio.PlayOneShotProvider, _) = saved;
                LocalizationManager.Instance.SetLanguage(saved.Item7);
            }
            return Task.CompletedTask;
        });

    private static AmbientBubble[] Darters() => BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsDarter == true).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Whistle_arms_halo_and_next_fresh_click_summons_at_the_cursor(bool key) => Run((toy, pointer, cues) =>
    {
        pointer(500, 400, true);   // the press that fired the hero button is still down
        if (key) ChaosRunHost.OnToyKey("Q"); else ChaosRunHost.UseToyById("rabbit_caller");
        Assert.Equal(20, toy.CooldownRemainingSec);
        Assert.Equal(3, ChaosRunHost.RabbitCallPending);
        Assert.True(ChaosRunHost.RabbitAimRunning);
        Assert.True(ChaosCursorGlowOverlay.Active!.HaloVisible);
        Assert.Contains("toy_ready", cues);
        Assert.Contains("R " + Loc.Get("Chaos_Rabbit_Whistle"), ChaosRunHost.State!.RecentEvents);

        ChaosRunHost.RabbitAimTick();   // arming press: ignored
        Assert.Empty(Darters());
        pointer(800, 600, false);
        ChaosRunHost.RabbitAimTick();
        pointer(800, 600, true);
        ChaosRunHost.RabbitAimTick();

        var darters = Darters();
        Assert.Equal(3, darters.Length);
        foreach (var d in darters)
        {
            Assert.InRange(d.Spec!.SpawnAtPxX!.Value, 740, 860);
            Assert.InRange(d.Spec.SpawnAtPxY!.Value, 540, 660);
        }
        Assert.Equal(0, ChaosRunHost.RabbitCallPending);
        Assert.False(ChaosRunHost.RabbitAimRunning);
        Assert.False(ChaosCursorGlowOverlay.Active!.HaloVisible);
        Assert.Contains("rabbit_spawn", cues);
        Assert.Contains("R " + Loc.GetF("Chaos_Rabbit_Answered", 3), ChaosRunHost.State!.RecentEvents);
    });

    [Fact]
    public Task Pause_holds_the_whistle_and_a_cooling_toy_does_not_rearm() => Run((toy, pointer, _) =>
    {
        pointer(100, 100, false);
        ChaosRunHost.UseToyById("rabbit_caller");
        ChaosRunHost.RabbitAimTick();   // the arming press lifts
        ChaosRunHost.ToggleManualPause();
        pointer(100, 100, true);
        ChaosRunHost.RabbitAimTick();
        Assert.Empty(Darters());
        Assert.Equal(3, ChaosRunHost.RabbitCallPending);
        Assert.True(ChaosRunHost.RabbitAimRunning);
        ChaosRunHost.ToggleManualPause();
        pointer(100, 100, false);
        ChaosRunHost.RabbitAimTick();
        pointer(100, 100, true);
        ChaosRunHost.RabbitAimTick();
        Assert.Equal(3, Darters().Length);
        ChaosRunHost.UseToyById("rabbit_caller");   // cooling down: no second whistle
        Assert.Equal(0, ChaosRunHost.RabbitCallPending);
    });

    [Fact]
    public Task Maxed_caller_storms_one_rabbit_per_1_25_s_for_10_s() => Run((toy, pointer, _) =>
    {
        pointer(100, 100, false);
        ChaosRunHost.UseToyById("rabbit_caller");
        ChaosRunHost.RabbitAimTick();   // the arming press lifts
        pointer(100, 100, true);
        ChaosRunHost.RabbitAimTick();
        Assert.Equal(3, Darters().Length);
        Assert.Equal(10, ChaosRunHost.RabbitStormRemaining);
        Assert.Contains(ChaosEffectBannerOverlay.Active!.Strip.Row, e => e.Text == "Rabbit Storm");
        Assert.Contains("R " + Loc.GetF("Chaos_Rabbit_Storm", 3), ChaosRunHost.State!.RecentEvents);
        for (int i = 0; i < 5; i++) ChaosRunHost.Tick();   // 1.25 s of run clock
        Assert.Equal(4, Darters().Length);
        Assert.True(toy.IsEffectActive);
        for (int i = 0; i < 40; i++) ChaosRunHost.Tick();
        Assert.Equal(0, ChaosRunHost.RabbitStormRemaining, 6);
        Assert.Equal(11, Darters().Length);
        Assert.False(toy.IsEffectActive);
    }, maxed: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Run_end_and_panic_stand_the_whistle_and_storm_down(bool panic) => Run((toy, pointer, _) =>
    {
        pointer(100, 100, false);
        ChaosRunHost.UseToyById("rabbit_caller");
        ChaosRunHost.RabbitAimTick();   // the arming press lifts
        Assert.True(ChaosRunHost.RabbitAimRunning);
        if (panic) PanicSurfaces.StopAll("test");
        else ChaosRunHost.RequestStop();
        Assert.Equal(0, ChaosRunHost.RabbitCallPending);
        Assert.False(ChaosRunHost.RabbitAimRunning);
        pointer(100, 100, true);
        ChaosRunHost.RabbitAimTick();
        Assert.Empty(Darters());
    });
}
