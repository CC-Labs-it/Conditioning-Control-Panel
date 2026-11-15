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

    [Fact]
    public void LightsDown_stock_tuning_is_the_shipped_effect()
    {
        var t = LightsDownTuning.Stock;
        Assert.Equal(LightsDownState.DimPerDepth, t.DimPerDepth);
        Assert.Equal(LightsDownState.RiseRate, t.RiseRate);
        Assert.Equal(LightsDownState.FallRate, t.FallRate);
        var s = new AppSettings();
        Assert.Equal(t, LightsDownOptions.Tuning(s.LightsDownDarkness, s.LightsDownDimSpeed, s.LightsDownReturnSpeed));
    }

    private static LightsDownFrame Run(LightsDownTuning t, double watch, double away, bool photosafe = false,
        LightsDownMotion motion = LightsDownMotion.Full)
    {
        var s = new LightsDownState { Tuning = t };
        var f = default(LightsDownFrame);
        for (double x = 0; x < watch; x += 1.0 / 30) f = s.Step(1.0 / 30, true, motion, photosafe);
        for (double x = 0; x < away; x += 1.0 / 30) f = s.Step(1.0 / 30, false, motion, photosafe);
        return f;
    }

    [Fact]
    public void LightsDown_darkness_scales_the_dim_only()
    {
        var stock = Run(LightsDownTuning.Stock, 8, 0);
        var dark = Run(LightsDownOptions.Tuning(100, 100, 100), 8, 0);
        var none = Run(LightsDownOptions.Tuning(0, 100, 100), 8, 0);
        Assert.Equal(stock.Depth, dark.Depth, 9);
        Assert.Equal(stock.ApertureAlpha, dark.ApertureAlpha, 9);
        Assert.True(dark.DimAlpha > stock.DimAlpha * 1.8);
        Assert.Equal(none.Beat, none.DimAlpha, 9);   // only the heartbeat is left
        Assert.True(dark.DimAlpha <= 1.0);
    }

    [Fact]
    public void LightsDown_dim_speed_changes_how_fast_the_room_falls()
    {
        double slow = Run(LightsDownOptions.Tuning(50, 50, 100), 2, 0).Depth;
        double stock = Run(LightsDownTuning.Stock, 2, 0).Depth;
        double fast = Run(LightsDownOptions.Tuning(50, 150, 100), 2, 0).Depth;
        Assert.True(slow < stock && stock < fast, $"{slow} {stock} {fast}");
    }

    [Fact]
    public void LightsDown_return_speed_moves_the_fall_but_never_the_photosafe_fade()
    {
        double slow = Run(LightsDownOptions.Tuning(50, 100, 50), 8, 0.3).Depth;
        double stock = Run(LightsDownTuning.Stock, 8, 0.3).Depth;
        double fast = Run(LightsDownOptions.Tuning(50, 100, 150), 8, 0.3).Depth;
        Assert.True(slow > stock && stock > fast, $"{slow} {stock} {fast}");

        for (int i = 0; i < 20; i++)
        {
            double away = i * 0.03;
            var a = Run(LightsDownTuning.Stock, 8, away, photosafe: true);
            var b = Run(LightsDownOptions.Tuning(50, 100, 50), 8, away, photosafe: true);
            var c = Run(LightsDownOptions.Tuning(50, 100, 150), 8, away, photosafe: true);
            Assert.Equal(a.Depth, b.Depth, 12);
            Assert.Equal(a.Depth, c.Depth, 12);
        }
        Assert.Equal(LightsDownOptions.StockFallRate, LightsDownOptions.FallRateFor(LightsDownOptions.Tuning(50, 100, 150), true));
    }

    [Fact]
    public void LightsDown_options_clamp()
    {
        var s = new AppSettings { LightsDownDarkness = 300, LightsDownDimSpeed = 1, LightsDownReturnSpeed = 999 };
        Assert.Equal(LightsDownOptions.DarknessMax, s.LightsDownDarkness);
        Assert.Equal(LightsDownOptions.DimSpeedMin, s.LightsDownDimSpeed);
        Assert.Equal(LightsDownOptions.ReturnSpeedMax, s.LightsDownReturnSpeed);
        // The fastest dim is still a slow exponential: no rise near the 300 ms Gentle floor.
        Assert.True(LightsDownOptions.Tuning(50, LightsDownOptions.DimSpeedMax, 100).RiseRate < 1.0);
    }

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
