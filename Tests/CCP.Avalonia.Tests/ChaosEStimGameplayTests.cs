using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosEStimGameplayTests
{
    // WPF ChaosModeService.cs:2761-2799 and BubbleService.cs:371-442, 4617-4626.
    [Fact]
    public Task Toy_charges_wait_for_a_player_pop_and_strike_the_nearest_eligible_bubbles() => WithRun(1, async state =>
    {
        state.ToyPower["e_stim"] = 1;
        state.EStimChargeMult = 2;
        var toy = Assert.Single(state.ActiveToys);
        ChaosRunHost.OnToyKey(toy.KeyLabel);
        Assert.Equal(2, BubbleOverlay.EStimChargesLeft);
        Assert.Equal(30, toy.CooldownRemainingSec);
        Assert.True(ChaosEStimGlow.Active!.IsVisible);
        ChaosRunHost.Tick();
        Assert.True(toy.IsEffectActive);

        var alone = Add(0);
        BubbleOverlay.Press(alone, alone.CenterX, alone.CenterY);
        Assert.Equal(2, BubbleOverlay.EStimChargesLeft); // No conductor in range.
        BubbleOverlay.Field.Bubbles.Clear();
        var automatic = Add(0);
        var unused = Add(100);
        BubbleOverlay.Pop(automatic); // A sweep is not a player click.
        Assert.Equal(2, BubbleOverlay.EStimChargesLeft);
        Assert.False(unused.Popping);
        BubbleOverlay.Field.Bubbles.Clear();

        var source = Add(0);
        var near = Add(100);
        var live = Add(200, new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
        var third = Add(300);
        var fourth = Add(400);
        var far = Add(700);
        var rabbit = Add(20, new EffectBubbleSpec { IsDarter = true });
        var freeze = Add(30, new EffectBubbleSpec { IsFreeze = true });
        var tease = Add(40, new EffectBubbleSpec { IsTease = true });
        var brittle = Add(50, new EffectBubbleSpec { IsBrittle = true });
        ChaosRunHost.ToggleManualPause();
        BubbleOverlay.Press(source, source.CenterX, source.CenterY);
        Assert.False(source.Popping);
        Assert.Equal(2, BubbleOverlay.EStimChargesLeft);
        ChaosRunHost.ToggleManualPause();
        BubbleOverlay.Press(source, source.CenterX, source.CenterY);
        Assert.Equal(1, BubbleOverlay.EStimChargesLeft);
        Assert.Equal(3, ChaosEStimOverlay.Active!.BoltCount);
        Assert.False(near.Popping); // Hops are delayed, not a synchronous sweep.
        await WaitUntil(() => near.Popping && live.Popping && third.Popping);
        Assert.False(live.Deflating);
        double scale = live.Scale;
        live.Step();
        Assert.True(live.Scale > scale); // WPF Pop expands a swept live, unlike a held defuse.
        Assert.Equal(1, state.Defused);
        Assert.All(new[] { fourth, far, rabbit, freeze, tease, brittle }, b => Assert.False(b.Popping));
        Assert.True(ChaosEStimGlow.Active!.IsVisible);

        BubbleOverlay.Field.Bubbles.Clear();
        source = Add(0);
        _ = Add(100);
        BubbleOverlay.Press(source, source.CenterX, source.CenterY);
        Assert.Equal(0, BubbleOverlay.EStimChargesLeft);
        Assert.False(ChaosEStimGlow.Active!.IsVisible);
        ChaosRunHost.Tick();
        Assert.False(toy.IsEffectActive);
    });

    [Fact]
    public Task Charge_and_arc_feed_use_the_active_language() => WithRun(1, state =>
    {
        var loc = LocalizationManager.Instance;
        string previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("fr");
            state.ToyPower["e_stim"] = 2;
            ChaosRunHost.UseToyById("e_stim");
            Assert.Equal("⚡ charge : vos 2 prochaines bulles conduisent", state.RecentEvents[0]);
            var source = Add(0);
            _ = Add(100);
            BubbleOverlay.Press(source, source.CenterX, source.CenterY);
            Assert.Equal("⚡ le courant jaillit (reste : 1)", state.RecentEvents[0]);
            BubbleOverlay.Field.Bubbles.Clear();
            source = Add(0);
            _ = Add(100);
            BubbleOverlay.Press(source, source.CenterX, source.CenterY);
            Assert.Equal("⚡ la charge est épuisée", state.RecentEvents[0]);
            state.ActiveToys.Single().CooldownRemainingSec = 0;
            state.MaxedBoons.Add("e_stim");
            ChaosRunHost.UseToyById("e_stim");
            Assert.Equal("⚡ charge : vos 2 prochaines bulles déclenchent une chaîne", state.RecentEvents[0]);
        }
        finally { loc.SetLanguage(previous); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Capstone_ripples_breadth_first_and_caps_the_chain_at_forty() => WithRun(3, async state =>
    {
        state.ToyPower["e_stim"] = 1;
        ChaosRunHost.UseToyById("e_stim");
        var source = Add(0);
        var first = Add(500);
        var second = Add(1000);
        var third = Add(1500);
        BubbleOverlay.Press(source, source.CenterX, source.CenterY);
        Assert.Equal(3, ChaosEStimOverlay.Active!.BoltCount);
        Assert.False(third.Popping);
        await WaitUntil(() => first.Popping && second.Popping && third.Popping);
        Assert.Equal(0, BubbleOverlay.EStimChargesLeft);

        BubbleOverlay.Field.Bubbles.Clear();
        state.ActiveToys.Single().CooldownRemainingSec = 0;
        ChaosRunHost.UseToyById("e_stim");
        source = Add(0);
        var cluster = Enumerable.Range(0, 41).Select(i => Add(100 + i)).ToArray();
        BubbleOverlay.Press(source, source.CenterX, source.CenterY);
        Assert.Equal(40, ChaosEStimOverlay.Active!.BoltCount);
        await WaitUntil(() => cluster.Take(40).All(b => b.Popping));
        Assert.False(cluster[40].Popping);
    });

    [Fact]
    public Task Pending_hops_do_not_pop_bubbles_after_shutdown() => WithRun(1, async state =>
    {
        state.ToyPower["e_stim"] = 1;
        ChaosRunHost.UseToyById("e_stim");
        var source = Add(0);
        var target = Add(100);
        BubbleOverlay.Press(source, source.CenterX, source.CenterY);
        Assert.Equal(1, ChaosEStimOverlay.Active!.BoltCount);
        ChaosRunHost.ForceShutdown();
        Assert.Equal(0, BubbleOverlay.EStimChargesLeft);
        Assert.Null(ChaosEStimGlow.Active);
        await Task.Delay(250);
        Assert.False(target.Popping);
    });

    private static AmbientBubble Add(double x, EffectBubbleSpec? spec = null)
    {
        var b = new AmbientBubble { X = x, Y = 100, Size = 20, Clickable = true, Spec = spec ?? new EffectBubbleSpec() };
        BubbleOverlay.Field.Bubbles.Add(b);
        return b;
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(3), "E-Stim hops did not finish");
            await Task.Delay(20);
        }
    }

    private static Task WithRun(int level, Func<ChaosRunState, Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var meta = ChaosMeta.State;
        var active = meta.ActiveLifetimeBoons.ToArray();
        int pockets = meta.ToyPockets;
        bool hadLevel = meta.LifetimeBoonLevels.TryGetValue("e_stim", out int oldLevel);
        var settings = CoreSettings.Current;
        bool skia = settings.ChaosSkiaFxEnabled;
        string key = settings.ChaosAccessoryKey1;
        var canShow = ChaosEStimOverlay.CanShow;
        var pointer = ChaosEStimGlow.ReadPointer;
        bool bannerChecks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        try
        {
            meta.ActiveLifetimeBoons.Clear();
            meta.ActiveLifetimeBoons.Add("e_stim");
            meta.LifetimeBoonLevels["e_stim"] = level;
            meta.ToyPockets = 1;
            settings.ChaosAccessoryKey1 = "Q";
            settings.ChaosSkiaFxEnabled = false;
            ChaosEStimOverlay.SkipPlatformChecksForTests = true;
            ChaosEStimOverlay.CanShow = () => true;
            ChaosEStimGlow.ReadPointer = () => (new PixelPoint(100, 100), false, false);
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            await test(ChaosRunHost.State!);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimGlow.ReadPointer = pointer;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = bannerChecks;
            settings.ChaosSkiaFxEnabled = skia;
            settings.ChaosAccessoryKey1 = key;
            meta.ActiveLifetimeBoons.Clear();
            foreach (var id in active) meta.ActiveLifetimeBoons.Add(id);
            meta.ToyPockets = pockets;
            if (hadLevel) meta.LifetimeBoonLevels["e_stim"] = oldLevel;
            else meta.LifetimeBoonLevels.Remove("e_stim");
        }
    });
}
