using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The option boxes of Undertow, Lights Down and Deck (owner, 2026-11-15): every default is the
/// effect exactly as it shipped, clamps hold, and each option moves what its label says.
/// </summary>
public class SuperLaneBOptionsTests
{
    private const double W = 1920;
    private const double Dt = 1.0 / 60;

    // ---------------- Undertow ----------------

    [Fact]
    public void Undertow_stock_tuning_is_the_shipped_lens()
    {
        var t = UndertowTuning.Stock;
        Assert.Equal(UndertowLens.MaxRadius, t.MaxRadius);
        Assert.Equal(UndertowLens.StillRadius, t.StillRadius);
        Assert.Equal(UndertowLens.MinRadius, t.MinRadius);
        Assert.Equal(UndertowLens.KickPerClick, t.KickPerClick);
        Assert.Equal(1.0, t.ShrinkScale);
        Assert.Equal(t, UndertowOptions.Tuning(100, 100, 100));
        var s = new AppSettings();
        Assert.Equal(t, UndertowOptions.Tuning(s.UndertowLensSize, s.UndertowShrinkSpeed, s.UndertowClickWiden));
    }

    [Fact]
    public void Undertow_stock_tuning_steps_exactly_as_the_old_lens()
    {
        var a = new UndertowState();
        var b = new UndertowState { Tuning = UndertowOptions.Tuning(100, 100, 100) };
        for (int i = 0; i < 240; i++)
        {
            UndertowLens.Step(a, Dt, 500 + i, 400, W, UndertowMotion.Full);
            UndertowLens.Step(b, Dt, 500 + i, 400, W, UndertowMotion.Full);
            if (i == 90) { UndertowLens.Click(a, 600, 400, W, UndertowMotion.Full); UndertowLens.Click(b, 600, 400, W, UndertowMotion.Full); }
        }
        Assert.Equal(a.Radius, b.Radius);
        Assert.Equal(a.DrawRadius, b.DrawRadius);
        Assert.Equal(a.Kick, b.Kick);
    }

    [Fact]
    public void Undertow_options_clamp()
    {
        Assert.Equal(UndertowOptions.SizeMin, UndertowOptions.ClampSize(0));
        Assert.Equal(UndertowOptions.SizeMax, UndertowOptions.ClampSize(999));
        Assert.Equal(0, UndertowOptions.ClampShrink(-5));
        Assert.Equal(UndertowOptions.WidenMax, UndertowOptions.ClampWiden(1000));
        var s = new AppSettings { UndertowLensSize = 10, UndertowShrinkSpeed = 500, UndertowClickWiden = -1 };
        Assert.Equal(UndertowOptions.SizeMin, s.UndertowLensSize);
        Assert.Equal(UndertowOptions.ShrinkMax, s.UndertowShrinkSpeed);
        Assert.Equal(UndertowOptions.WidenMin, s.UndertowClickWiden);
        Assert.Equal(105, UndertowOptions.Snap(103));
    }

    [Fact]
    public void Undertow_lens_size_caps_the_widest_lens_and_keeps_the_floor()
    {
        var s = new UndertowState { Tuning = UndertowOptions.Tuning(50, 100, 100) };
        for (int i = 0; i < 120; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        Assert.True(s.Radius <= 0.25 * W + 1e-9, $"{s.Radius / W:F3} W");
        for (int i = 0; i < 60 * 60; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(UndertowLens.MinRadius * 0.5 * W, s.Radius, 6);

        var big = new UndertowState { Tuning = UndertowOptions.Tuning(150, 100, 100) };
        double peak = 0;
        for (int i = 0; i < 120; i++) { UndertowLens.Step(big, Dt, 500, 400, W, UndertowMotion.Full); peak = Math.Max(peak, big.Radius); }
        Assert.True(peak > 0.5 * W, $"{peak / W:F3} W");
        Assert.True(peak <= 0.75 * W + 1e-9);
    }

    [Fact]
    public void Undertow_shrink_speed_zero_holds_and_double_drains_faster()
    {
        double R(int shrink)
        {
            var s = new UndertowState { Tuning = UndertowOptions.Tuning(100, shrink, 100) };
            for (int i = 0; i < 60 * 4; i++) UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Full);
            return s.Radius;
        }
        Assert.Equal(UndertowLens.MaxRadius * W, R(0), 6);
        Assert.True(R(200) < R(100));
        Assert.True(R(100) < R(0));
    }

    [Fact]
    public void Undertow_click_widen_scales_the_kick_and_zero_only_ripples()
    {
        var s = new UndertowState { Tuning = UndertowOptions.Tuning(100, 100, 0) };
        UndertowLens.Step(s, 0, 500, 400, W, UndertowMotion.Full);
        s.Kick = 0;
        UndertowLens.Click(s, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(0, s.Kick);
        Assert.Contains(s.Ripples, r => r.Live);

        var d = new UndertowState { Tuning = UndertowOptions.Tuning(100, 100, 200) };
        UndertowLens.Step(d, 0, 500, 400, W, UndertowMotion.Full);
        d.Kick = 0;
        UndertowLens.Click(d, 500, 400, W, UndertowMotion.Full);
        Assert.Equal(2 * UndertowLens.KickPerClick * W, d.Kick, 6);
    }

    [Fact]
    public void Undertow_still_lens_follows_the_size()
    {
        var s = new UndertowState { Tuning = UndertowOptions.Tuning(150, 100, 100) };
        UndertowLens.Step(s, Dt, 500, 400, W, UndertowMotion.Off);
        Assert.Equal(UndertowLens.StillRadius * 1.5 * W, s.Radius, 6);
    }

    // ---------------- Lights Down ----------------
    // Reworked 2026-11-17: its options are pinned in LightsDownReworkTests.

    // ---------------- Deck ----------------

    [Fact]
    public void Deck_stock_is_the_shipped_roll_and_reach()
    {
        var s = new AppSettings();
        Assert.Equal((FlickerDeck.BreakAtMin, FlickerDeck.BreakAtMax), DeckOptions.BreakRange(s.DeckFlips));
        Assert.Equal(1.6, DeckOptions.ReachSides(s.DeckBreakReach));

        // Same seed, same card: the stock overload and the option path roll identically.
        var a = FlickerDeck.Create(2, new Random(7));
        var (lo, hi) = DeckOptions.BreakRange(DeckOptions.FlipsDefault);
        var b = FlickerDeck.Create(2, new Random(7), lo, hi);
        Assert.Equal(a.BreakAt, b.BreakAt);
        Assert.Equal(a.Clock, b.Clock);
    }

    [Fact]
    public void Deck_flips_moves_the_roll_and_never_goes_under_one()
    {
        Assert.Equal((1, 2), DeckOptions.BreakRange(1));
        Assert.Equal((1, 2), DeckOptions.BreakRange(-3));
        Assert.Equal((6, 8), DeckOptions.BreakRange(7));
        Assert.Equal((6, 8), DeckOptions.BreakRange(50));
        var rng = new Random(3);
        for (int i = 0; i < 200; i++)
        {
            var c = FlickerDeck.Create(0, rng, 6, 8);
            Assert.InRange(c.BreakAt, 6, 8);
        }
    }

    [Fact]
    public void Deck_one_flip_card_breaks_on_the_first_tap()
    {
        var c = FlickerDeck.Create(0, new Random(1), 1, 1);
        Assert.Equal(FlickerPress.Shatter, FlickerDeck.Press(c, MotionLevel.Full));
    }

    [Fact]
    public void Deck_reach_scales_and_clamps()
    {
        Assert.Equal(0, DeckOptions.ReachSides(0));
        Assert.Equal(3.2, DeckOptions.ReachSides(200), 9);
        Assert.Equal(3.2, DeckOptions.ReachSides(900), 9);
        var s = new AppSettings { DeckFlips = 0, DeckBreakReach = -10 };
        Assert.Equal(DeckOptions.FlipsMin, s.DeckFlips);
        Assert.Equal(DeckOptions.ReachMin, s.DeckBreakReach);
    }
}
