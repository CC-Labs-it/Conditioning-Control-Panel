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
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ChaosModeService run barks (:1037, :1074, :1468, :1564-1582, :1890, :1962-2132, :2391, :3080-3110)
/// raised by the Avalonia run host with the BarkService context keys.</summary>
public sealed class ChaosRunBarkTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void FreshMeta()
    {
        for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
        ChaosMeta.Init();
    }

    private static Task WithRun(bool drafts, Action<List<BarkContext>> body, int waves = 3) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); FreshMeta();
        var heard = new List<BarkContext>();
        var was = ChaosRunHost.RunBark;
        var firer = EffectPayload.Firer;
        try
        {
            EffectPayload.Firer = null;
            ChaosRunHost.RunBark = (t, f) => { var c = new BarkContext(t); f?.Invoke(c); heard.Add(c); };
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = waves, BoonDraftEnabled = drafts });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            heard.Clear();
            body(heard);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.RunBark = was; EffectPayload.Firer = firer;
            BubbleOverlay.Field.Clock = () => DateTime.UtcNow;
            FreshMeta();
        }
        return Task.CompletedTask;
    });

    private static BarkContext One(List<BarkContext> heard, string trigger) => Assert.Single(heard, c => c.Trigger == trigger);

    [Fact]
    public Task Treat_pop_barks_benign_and_combo_milestone() => WithRun(false, heard =>
    {
        ChaosRunHost.State!.Combo = 9;
        ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = new EffectBubbleSpec { VariantId = "flash", Payload = new FlashPayload() } });
        var c = One(heard, "ChaosBenignPopped");
        Assert.Equal(("flash", 10.0), ((string)c.Values["variant_id"], (double)c.Values["combo"]));
        Assert.Equal(10.0, One(heard, "ChaosComboMilestone").Values["combo"]);
        ChaosRunHost.State!.Combo = 24;
        ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = new EffectBubbleSpec { VariantId = "flash", Payload = new FlashPayload() } });
        Assert.Equal(25.0, One(heard, "ChaosComboBig").Values["threshold"]);
    });

    [Fact]
    public Task Freeze_catch_barks_points_and_combo() => WithRun(false, heard =>
    {
        ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = new EffectBubbleSpec { VariantId = "freeze", IsFreeze = true, Payload = new FlashPayload() } });
        var c = One(heard, "ChaosFreezeCaught");
        Assert.Equal(1.0, c.Values["combo"]);
        Assert.True((double)c.Values["points"] > 0);
    });

    [Fact]
    public Task Hold_defuse_and_no_focus_bark_first_times_once() => WithRun(false, heard =>
    {
        var state = ChaosRunHost.State!;
        state.Focus = 100;
        var t = DateTime.UnixEpoch;
        BubbleOverlay.Field.Clock = () => t = t.AddMilliseconds(32);
        EffectBubbleSpec Live() => new() { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() };
        var b = BubbleOverlay.SpawnChaos(Live());
        BubbleOverlay.Press(b, b.CenterX, b.CenterY);
        for (int i = 0; i < 40 && !b.Popping; i++) BubbleOverlay.Field.Step();
        One(heard, "ChaosDefuseFirst");
        Assert.Equal("pink", One(heard, "ChaosBubbleDefused").Values["payload"]);
        Assert.True(ChaosMeta.State.SeenBarkDefuseFirst);
        for (int n = 0; n < 2; n++)
        {
            state.Focus = 0; state.Shields = 0; state.Combo = 7;
            var c = BubbleOverlay.SpawnChaos(Live());
            BubbleOverlay.Press(c, c.CenterX, c.CenterY);
        }
        One(heard, "ChaosDefuseNoFocus");
        var det = heard.Where(x => x.Trigger == "ChaosBubbleDetonated").ToList();
        Assert.Equal(2, det.Count);
        Assert.Equal((7.0, 2.0, false), ((double)det[1].Values["combo"], (double)det[1].Values["run_detonations"], (bool)det[1].Values["shield_absorbed"]));
    });

    [Fact]
    public Task Shielded_detonation_barks_absorbed() => WithRun(false, heard =>
    {
        var state = ChaosRunHost.State!;
        state.Shields = 2;
        ChaosRunHost.OnDetonated(new AmbientBubble { Spec = new EffectBubbleSpec { VariantId = "pink", IsLive = true, Payload = new FlashPayload() } });
        var c = One(heard, "ChaosBubbleDetonatedAbsorbed");
        Assert.Equal((1.0, true), ((double)c.Values["shields_left"], (bool)c.Values["shield_absorbed"]));
        Assert.DoesNotContain(heard, x => x.Trigger == "ChaosBubbleDetonated");
    });

    [Theory]
    [InlineData(0), InlineData(1), InlineData(2)]
    public Task Draft_barks_clear_then_skip_boon_or_curse(int pick) => WithRun(true, heard =>
    {
        for (int i = 0; i < 80; i++) ChaosRunHost.Tick();
        Assert.Equal(1.0, One(heard, "ChaosWaveCleared").Values["wave"]);
        // WPF :1468 then :1474: cleared must win the bark engine's 60 s gap over escalated.
        int cleared = heard.FindIndex(x => x.Trigger == "ChaosWaveCleared");
        Assert.True(cleared < heard.FindIndex(x => x.Trigger == "ChaosWaveEscalated"));
        Assert.Equal(2.0, One(heard, "ChaosWaveEscalated").Values["wave"]);
        var card = pick == 0 ? null : ChaosBoonPool.All.First(b => b.IsCurse == (pick == 2));
        ChaosRunHost.ChooseBoon(card);
        string expect = pick switch { 0 => "ChaosBoonSkipped", 1 => "ChaosBoonPicked", _ => "ChaosCursePicked" };
        var c = One(heard, expect);
        if (card != null) Assert.Equal(card.Name, c.Values["boon"]);
        else Assert.Equal((double)ChaosRunHost.State!.Shields, c.Values["shields_now"]);
    });

    [Fact]
    public Task Ending_soon_and_act_change_bark_once() => WithRun(false, heard =>
    {
        for (int i = 0; i < 230; i++) ChaosRunHost.Tick();
        One(heard, "ChaosEndingSoon");
        Assert.Contains(heard, x => x.Trigger == "ChaosWaveEscalated");   // WPF :1453, drafts off
        var act = One(heard, "ChaosActChanged");
        Assert.Equal(2.0, act.Values["act"]);
    }, waves: ChaosRunEngine.WAVES_PER_ACT + 1);

    [Fact]
    public Task Focus_low_with_a_live_on_screen_barks_once_after_eight_seconds() => WithRun(false, heard =>
    {
        BubbleOverlay.SpawnChaos(new EffectBubbleSpec { VariantId = "pink", IsLive = true, FuseMs = 600000, SizePx = 200, Payload = new FlashPayload() });
        int ticks = (int)(ChaosTuning.FOCUS_LOW_BARK_SEC / ChaosRunEngine.TICK_SEC);
        for (int i = 0; i < ticks - 1; i++) { ChaosRunHost.State!.Focus = 0; ChaosRunHost.Tick(); }
        Assert.DoesNotContain(heard, x => x.Trigger == "ChaosFocusLow");
        for (int i = 0; i < ticks; i++) { ChaosRunHost.State!.Focus = 0; ChaosRunHost.Tick(); }
        One(heard, "ChaosFocusLow");
    });
}
