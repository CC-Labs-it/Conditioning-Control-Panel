using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosFreezeGameplayTests
{
    [Fact]
    public Task Frozen_channel_accepts_no_focus_then_rejects_it_after_expiry() => WithRun(state =>
    {
        state.Focus = 0;
        Fire();
        var bubble = Live();
        BubbleOverlay.Press(bubble, bubble.CenterX, bubble.CenterY);
        Assert.True(bubble.Channeling);
        CompleteChannel();
        Assert.Equal((1, 0, 0d), (state.Defused, state.Detonated, state.Focus));

        Expire();
        var next = Live();
        BubbleOverlay.Press(next, next.CenterX, next.CenterY);
        Assert.Equal((1, "nofocus"), (state.Detonated, next.BrokenReason));
        Assert.False(next.Channeling);
    });

    [Fact]
    public Task Frozen_channel_costs_nothing_then_normal_channels_cost_focus() => WithRun(state =>
    {
        state.Focus = 80;
        Fire();
        var bubble = Live();
        BubbleOverlay.Press(bubble, bubble.CenterX, bubble.CenterY);
        Assert.True(bubble.Channeling);
        CompleteChannel();
        Assert.Equal((1, 80d), (state.Defused, state.Focus));

        Expire();
        var next = Live();
        BubbleOverlay.Press(next, next.CenterX, next.CenterY);
        Assert.True(next.Channeling);
        CompleteChannel();
        Assert.Equal((2, 80d - ChaosTuning.DEFUSE_COST), (state.Defused, state.Focus));
    });

    [Fact]
    public Task Freeze_stops_spawning_until_it_expires() => WithRun(state =>
    {
        state.PrismChance = 0;
        ChaosRunHost.SpawnTick();
        Assert.Equal(1, BubbleOverlay.ChaosCount);
        Fire();
        for (int i = 0; i < 4; i++) ChaosRunHost.SpawnTick();
        Assert.Equal(1, BubbleOverlay.ChaosCount);
        Expire();
        ChaosRunHost.SpawnTick();
        Assert.Equal(2, BubbleOverlay.ChaosCount);
    });

    [Fact]
    public Task Only_maxed_trigger_sweeps_live_bubbles_and_field_resumes_after_expiry() => WithRun(state =>
    {
        var first = Live();
        Fire();
        Assert.False(first.Popping); // Normal trigger does not sweep.
        Expire();
        double fuse = first.FuseRemainingMs;
        BubbleOverlay.Field.Step();
        Assert.True(first.FuseRemainingMs < fuse);

        var second = Live();
        var treat = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
            { VariantId = "pink", SizePx = 200, Payload = new FlashPayload() });
        state.MaxedBoons.Add("freeze_trigger");
        double before = state.Focus;
        Fire();
        Assert.True(first.Popping);
        Assert.True(second.Popping);
        Assert.False(treat.Popping);
        Assert.Equal(2, state.Defused);
        Assert.Equal(before, state.Focus);
        Assert.True(state.Score > 0);

        Expire();
        var later = Live();
        fuse = later.FuseRemainingMs;
        BubbleOverlay.Field.Step();
        Assert.True(later.FuseRemainingMs < fuse);
        state.MaxedBoons.Remove("freeze_trigger");
        Fire();
        Assert.False(later.Popping);
        Assert.Equal(2, state.Defused);
    });

    [Fact]
    public Task Freeze_pickups_spawn_and_are_capped_at_two() => WithRun(state =>
    {
        state.Config.EnabledVariants = new List<string> { "bambifreeze" };
        state.PrismChance = 0;
        ChaosRunHost.SpawnTick();
        ChaosRunHost.SpawnTick();
        ChaosRunHost.SpawnTick();
        Assert.Equal(2, BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsFreeze == true));
        Assert.Equal(3, BubbleOverlay.ChaosCount);
    });

    [Fact]
    public Task Pickup_refreshes_one_banner_and_holds_the_field_until_expiry() => WithRun(state =>
    {
        var live = Live();
        double y = live.Y, fuse = live.FuseRemainingMs;
        var pickup = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.Build(
            ChaosBubbleVariants.All.Single(v => v.Id == "bambifreeze"), 0));
        double expectedScore = 140 * state.TotalMult;
        int combo = state.Combo;
        double heat = state.Heat;
        BubbleOverlay.PopChaos(pickup);
        Assert.True(BubbleOverlay.Field.FreezeChaos);
        Assert.Equal(expectedScore, state.Score);
        Assert.Equal(combo + 1, state.Combo);
        Assert.Equal(Math.Min(1.0, heat + 0.05), state.Heat);
        var strip = ChaosEffectBannerOverlay.Active!.Strip;
        Assert.True(strip.IsLive("freeze"));
        BubbleOverlay.Field.Step();
        Assert.Equal(y, live.Y);
        Assert.Equal(fuse, live.FuseRemainingMs);
        for (int i = 0; i < 8; i++) ChaosRunHost.Tick();
        var refresh = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.Build(
            ChaosBubbleVariants.All.Single(v => v.Id == "bambifreeze"), 0));
        BubbleOverlay.PopChaos(refresh);
        Assert.Single(strip.Row);
        Assert.Equal(3.5, ChaosRunHost.FreezeRemainingSec);
        ChaosRunHost.ToggleManualPause();
        for (int i = 0; i < 14; i++) ChaosRunHost.Tick();
        Assert.True(strip.IsLive("freeze"));
        Assert.True(BubbleOverlay.Field.FreezeChaos);
        ChaosRunHost.ToggleManualPause();
        Expire();
        Assert.False(BubbleOverlay.Field.FreezeChaos);
        Assert.False(strip.IsLive("freeze"));
        BubbleOverlay.Field.Step();
        Assert.True(live.FuseRemainingMs < fuse);
        Fire();
        Assert.True(strip.IsLive("freeze"));
        ChaosRunHost.ForceShutdown();
        Assert.Null(ChaosEffectBannerOverlay.Active);
        Assert.False(BubbleOverlay.Field.FreezeChaos);
    });

    private static AmbientBubble Live() => BubbleOverlay.SpawnChaos(new EffectBubbleSpec
        { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });

    private static void Fire() => ChaosRunHost.UseToyById("freeze_trigger");
    private static void Expire() { for (int i = 0; i < 14; i++) ChaosRunHost.Tick(); }
    private static void CompleteChannel()
    {
        var now = BubbleOverlay.Field.Clock();
        BubbleOverlay.Field.Clock = () => now.AddMilliseconds(ChaosTuning.DEFUSE_HOLD_MS + 1);
        BubbleOverlay.Field.Step();
    }

    private static Task WithRun(Action<ChaosRunState> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var meta = ChaosMeta.State;
        int sparks = meta.Sparks;
        bool hadAward = meta.FirstTimesAwarded.Contains(ChaosFirstTimes.Play);
        bool checks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        var clock = BubbleOverlay.Field.Clock;
        try
        {
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, EnabledVariants = new List<string> { "flash" } });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Clock = () => DateTime.UnixEpoch;
            var state = ChaosRunHost.State!;
            state.MaxedBoons.Remove("freeze_trigger");
            state.ActiveToys.Clear();
            state.ActiveToys.Add(new ChaosToyState { Id = "freeze_trigger", ChargesLeft = 3 });
            test(state);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            BubbleOverlay.Field.Clock = clock;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = checks;
            meta.Sparks = sparks;
            if (hadAward) meta.FirstTimesAwarded.Add(ChaosFirstTimes.Play);
            else meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        }
        return Task.CompletedTask;
    });
}
