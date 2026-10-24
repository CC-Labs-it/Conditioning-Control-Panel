using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF Bubble's live fuse, hold-to-defuse channel and detonation (BubbleService.cs
/// :4147-4212, OnPlayerPress :4448, TickChannel :4515) and ChaosModeService OnDefused/OnDetonated's
/// state change, on Core's stepped AmbientBubble.</summary>
public class ChaosLiveBubbleTests
{
    private static EffectBubbleSpec Live(int fuseMs) => new()
        { VariantId = "pink", IsLive = true, FuseMs = fuseMs, SizePx = 200, Payload = new FlashPayload { Strength = 50 } };

    private static (AmbientBubbleField F, AmbientBubble B, List<string> Log) Field(EffectBubbleSpec spec)
    {
        var t = DateTime.UnixEpoch;
        var f = new AmbientBubbleField { Clock = () => t = t.AddMilliseconds(32) };   // 32 ms of wall time a read
        var b = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 1920, 1080, 1, spec);
        f.Bubbles.Add(b);
        var log = new List<string>();
        f.ChaosDetonated = x => log.Add("det:" + (x.BrokenReason ?? "fuse"));
        f.ChaosDefused = x => log.Add(x.DefusedViaChannel ? "snap:held" : "snap:swept");
        return (f, b, log);
    }

    [Fact]
    public void The_fuse_burns_32ms_a_step_and_fuse_out_detonates_once()
    {
        var (f, b, log) = Field(Live(320));
        for (int i = 0; i < 9; i++) f.Step();
        Assert.Empty(log);
        f.Step();
        Assert.Equal(new[] { "det:fuse" }, log);
        Assert.True(b.Popping);
        for (int i = 0; i < 60; i++) f.Step();   // the burst plays out, no second report
        Assert.Single(log);
        Assert.Empty(f.Bubbles);
    }

    [Fact]
    public void A_live_that_escapes_detonates_and_a_treat_that_escapes_is_a_miss()
    {
        var (f, _, log) = Field(Live(int.MaxValue));
        int misses = 0;
        for (int i = 0; i < 100_000 && f.Bubbles.Count > 0; i++) misses += f.Step();
        Assert.Equal(("det:fuse", 1, 0), (log[0], log.Count, misses));

        var (g, _, log2) = Field(new EffectBubbleSpec { VariantId = "flash", SizePx = 200 });
        misses = 0;
        for (int i = 0; i < 100_000 && g.Bubbles.Count > 0; i++) misses += g.Step();
        Assert.Equal((0, 1), (log2.Count, misses));
    }

    [Fact]
    public void A_full_hold_snaps_with_the_bubble_pinned_and_the_fuse_paused()
    {
        var (f, b, log) = Field(Live(5000));
        for (int i = 0; i < 5; i++) f.Step();
        double fuse = b.FuseRemainingMs, x = b.X, y = b.Y;
        f.PressLive(b, canChannel: true, b.CenterX, b.CenterY);
        Assert.True(b.Channeling);
        for (int i = 0; i < 31; i++) f.Step();   // 31 x 32 ms < DEFUSE_HOLD_MS
        Assert.Equal((fuse, x, y, 0), (b.FuseRemainingMs, b.X, b.Y, log.Count));
        Assert.True(b.ChannelScale < 1);
        f.Step();
        Assert.Equal(new[] { "snap:held" }, log);
        Assert.Equal(fuse / 1000.0, b.DefuseJudgeFuseSec, 6);
        Assert.True(b.Deflating);
    }

    [Theory]
    [InlineData(2, false, true, "det:click")]     // let go under CLICK_THRESHOLD_MS
    [InlineData(10, false, true, "det:release")]  // let go later
    [InlineData(10, true, false, "det:release")]  // the cursor strayed off the bubble
    public void A_broken_hold_detonates_with_its_reason(int steps, bool held, bool onBubble, string expected)
    {
        var (f, b, log) = Field(Live(5000));
        f.PressLive(b, canChannel: true, b.CenterX, b.CenterY);
        for (int i = 0; i < steps; i++) f.Step();
        f.Held = held;
        if (!onBubble) f.CursorX = b.X - 500;
        f.Step();
        Assert.Equal(new[] { expected }, log);
    }

    /// <summary>WPF TickChannel (:4531) measures the hold on the wall clock: one late step after a
    /// full second snaps it, however few steps ran.</summary>
    [Fact]
    public void The_hold_is_wall_time_not_steps()
    {
        var (f, b, log) = Field(Live(5000));
        var t = DateTime.UnixEpoch;
        f.Clock = () => t;
        f.PressLive(b, canChannel: true, b.CenterX, b.CenterY);
        t = t.AddMilliseconds(ChaosTuning.DEFUSE_HOLD_MS - 1);
        f.Step();
        Assert.Empty(log);
        t = t.AddMilliseconds(1);
        f.Step();
        Assert.Equal(new[] { "snap:held" }, log);
    }

    [Fact]
    public void No_focus_triggers_in_the_grip_and_a_held_field_cancels_the_hold()
    {
        var (f, b, log) = Field(Live(5000));
        f.PressLive(b, canChannel: false, b.CenterX, b.CenterY);
        Assert.Equal(new[] { "det:nofocus" }, log);

        var (g, c, log2) = Field(Live(5000));
        g.PressLive(c, canChannel: true, c.CenterX, c.CenterY);
        g.HoldChaos = true;
        g.Step();
        Assert.False(c.Channeling);
        g.PressLive(c, canChannel: false, c.CenterX, c.CenterY);   // a held field swallows presses too
        Assert.False(c.Popping);
        Assert.Empty(log2);
    }

    [Fact]
    public void DefuseAllLive_snaps_only_the_lives()
    {
        var (f, b, log) = Field(Live(5000));
        var treat = AmbientBubble.SpawnChaos(new Random(2), 0, 0, 0, 1920, 1080, 1, new EffectBubbleSpec { SizePx = 150 });
        f.Bubbles.Add(treat);
        f.DefuseAllLive();
        Assert.Equal(new[] { "snap:swept" }, log);
        Assert.False(treat.Popping);
        Assert.Equal(b.FuseRemainingMs / 1000.0, b.DefuseJudgeFuseSec, 6);
    }

    private static ChaosRunState State() => new(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });

    [Fact]
    public void A_held_snap_pays_its_focus_cost_and_wpf_points()
    {
        var s = State();
        s.Focus = 100;
        double heat = s.Heat, focus = s.Focus;
        var spec = Live(5000);
        var (pts, lb, sb) = ChaosFieldMath.ApplyDefuse(s, spec, 3.0, viaChannel: true, free: false, 1.0, 1.0);
        double expected = ChaosRunEngine.BasePoints(spec.Strength) * s.TotalMult * s.BlindfoldPayMult;
        Assert.Equal((expected, 1.0, 1.0), (pts, lb, sb));
        Assert.Equal((1, 1, focus - ChaosTuning.DEFUSE_COST), (s.Defused, s.Combo, s.Focus));
        Assert.Equal(Math.Min(1.0, heat + 0.07), s.Heat, 6);
        Assert.Equal(expected, s.Score, 6);

        // A swept snap is free; Last Breath and a maxed Slowburner pay at the brink.
        s.LastBreathWindowSec = 0.5; s.LastBreathPayMult = 4; s.MaxedBoons.Add("slowburner");
        double f2 = s.Focus;
        var (pts2, lb2, sb2) = ChaosFieldMath.ApplyDefuse(s, spec, 0.4, viaChannel: false, free: false, 1.0, 1.0);
        Assert.Equal((4.0, 3.0, f2), (lb2, sb2, s.Focus));
        Assert.Equal(ChaosRunEngine.BasePoints(spec.Strength) * 12 * s.TotalMult, pts2, 6);   // TotalMult after the combo/heat bump
    }

    [Fact]
    public void A_detonation_counts_then_the_cheapest_protection_pays()
    {
        var s = State();
        s.Shields = 1; s.CollarSaves = 1; s.Combo = 7; s.Heat = 0.5;
        Assert.Equal(ChaosFieldMath.Detonation.SnapChainHeld, ChaosFieldMath.ApplyDetonation(s, invulnerable: true));
        Assert.Equal((1, 1, 1, 7), (s.Detonated, s.EffectsFired, s.Shields, s.Combo));
        Assert.Equal(ChaosFieldMath.Detonation.ShieldAbsorbed, ChaosFieldMath.ApplyDetonation(s, false));
        Assert.Equal((0, 7), (s.Shields, s.Combo));
        Assert.Equal(0.3, s.Heat, 6);
        Assert.Equal(ChaosFieldMath.Detonation.CollarHeld, ChaosFieldMath.ApplyDetonation(s, false));
        Assert.Equal((0, 7), (s.CollarSaves, s.Combo));
        Assert.Equal(ChaosFieldMath.Detonation.Hit, ChaosFieldMath.ApplyDetonation(s, false));
        Assert.Equal((4, 0, 0.0), (s.Detonated, s.Combo, s.Heat));
    }
}
