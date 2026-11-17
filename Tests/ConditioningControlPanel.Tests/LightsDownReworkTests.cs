using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Lights Down as reworked by the owner (2026-11-17): full screen = a small dim at the screen
/// edges that pulls back while the mouse moves (or the eyes leave) and comes back when it rests;
/// floating = the whole screen dims from the edges in, except a soft hole and a mod-coloured halo
/// around every floating video. Plus the mini game reading as off while Floating is the pick.
/// </summary>
public class LightsDownReworkTests
{
    private const double Dt = 1.0 / 30;
    private const double W = 1920, H = 1080;

    // ---------------- options ----------------

    [Fact]
    public void Stock_tuning_is_the_default_settings()
    {
        var s = new AppSettings();
        Assert.Equal(LightsDownTuning.Stock, LightsDownOptions.Tuning(s.LightsDownDarkness, s.LightsDownDimSpeed, s.LightsDownReturnSpeed));
        Assert.Equal(LightsDownOptions.StockInRate, LightsDownTuning.Stock.InRate, 12);
        Assert.Equal(LightsDownOptions.StockOutRate, LightsDownTuning.Stock.OutRate, 12);
    }

    [Fact]
    public void Options_clamp()
    {
        var s = new AppSettings { LightsDownDarkness = 300, LightsDownDimSpeed = 1, LightsDownReturnSpeed = 999 };
        Assert.Equal(LightsDownOptions.DarknessMax, s.LightsDownDarkness);
        Assert.Equal(LightsDownOptions.DimSpeedMin, s.LightsDownDimSpeed);
        Assert.Equal(LightsDownOptions.ReturnSpeedMax, s.LightsDownReturnSpeed);
    }

    [Fact]
    public void Darkness_is_monotonic_from_nothing_to_the_cap()
    {
        Assert.Equal(0, LightsDownOptions.PeakAlpha(0), 12);
        Assert.Equal(LightsDownOptions.MaxAlpha, LightsDownOptions.PeakAlpha(100), 12);
        double last = -1;
        for (int d = 0; d <= 100; d += 5)
        {
            double a = LightsDownOptions.PeakAlpha(d);
            Assert.True(a > last, $"darkness {d}");
            last = a;
        }
        double stock = LightsDownOptions.PeakAlpha(LightsDownOptions.DarknessDefault);
        Assert.InRange(stock, 0.55, 0.7);
    }

    [Fact]
    public void Gentle_only_ever_lengthens_the_pull_back()
    {
        foreach (int ret in new[] { 50, 100, 150 })
        {
            var t = LightsDownOptions.Tuning(50, 100, ret);
            Assert.Equal(t.OutRate, LightsDownOptions.OutRateFor(t, 0), 12);
            foreach (double fade in new[] { 0.25, 0.5, 0.75, 2.0 })
            {
                double r = LightsDownOptions.OutRateFor(t, fade);
                Assert.True(r <= t.OutRate + 1e-12);
                Assert.True(1 / r >= Math.Max(0.3, fade) - 1e-9, $"{ret} {fade}: a {1 / r:0.000} s pull back");
            }
        }
    }

    // ---------------- full screen: retract in / out ----------------

    private static LightsDownFullScreen Rest(double seconds, LightsDownTuning? t = null)
    {
        var s = new LightsDownFullScreen();
        for (double x = 0; x < seconds; x += Dt) s.Step(Dt, false, false, t ?? LightsDownTuning.Stock, 0);
        return s;
    }

    [Fact]
    public void A_resting_mouse_brings_the_dim_in_and_it_settles_full()
    {
        var s = Rest(6);
        Assert.True(s.Level > 0.98, $"{s.Level}");
        Assert.True(s.Level <= 1);
    }

    [Fact]
    public void Moving_the_mouse_pulls_it_back_and_it_waits_for_a_rest_before_coming_back()
    {
        var t = LightsDownTuning.Stock;
        var s = Rest(6);
        // Keep moving: gone within 1 / OutRate seconds (0.4 s), linear.
        double half = 0.5 / t.OutRate;
        for (double x = 0; x < half; x += Dt) s.Step(Dt, true, false, t, 0);
        Assert.InRange(s.Level, 0.35, 0.62);
        for (double x = 0; x < half + 0.1; x += Dt) s.Step(Dt, true, false, t, 0);
        Assert.Equal(0, s.Level, 9);

        // The mouse stops: nothing for RestSeconds, then it eases back in.
        for (double x = 0; x < LightsDownFullScreen.RestSeconds - 0.1; x += Dt) s.Step(Dt, false, false, t, 0);
        Assert.Equal(0, s.Level, 9);
        for (double x = 0; x < 1.5; x += Dt) s.Step(Dt, false, false, t, 0);
        Assert.InRange(s.Level, 0.4, 0.9);
    }

    [Fact]
    public void Looking_away_pulls_it_back_like_a_moving_mouse()
    {
        var a = Rest(6);
        var b = Rest(6);
        for (int i = 0; i < 6; i++)
        {
            a.Step(Dt, true, false, LightsDownTuning.Stock, 0);
            b.Step(Dt, false, true, LightsDownTuning.Stock, 0);
        }
        Assert.Equal(a.Level, b.Level, 12);
        Assert.True(a.Level < 0.95);
    }

    [Fact]
    public void Dim_speed_and_return_speed_move_what_their_labels_say()
    {
        double In(int dim) => Rest(LightsDownFullScreen.RestSeconds + 1, LightsDownOptions.Tuning(50, dim, 100)).Level;
        Assert.True(In(50) < In(100) && In(100) < In(150));

        double Out(int ret)
        {
            var t = LightsDownOptions.Tuning(50, 100, ret);
            var s = Rest(6, t);
            for (int i = 0; i < 4; i++) s.Step(Dt, true, false, t, 0);
            return s.Level;
        }
        Assert.True(Out(50) > Out(100) && Out(100) > Out(150));
    }

    [Fact]
    public void A_hitch_never_jumps_the_dim()
    {
        var s = new LightsDownFullScreen();
        s.Step(5, false, false, LightsDownTuning.Stock, 0);   // one 5 s frame counts as 0.25 s
        Assert.True(s.Level < 0.25);
        s.Step(double.NaN, true, false, LightsDownTuning.Stock, 0);
        Assert.False(double.IsNaN(s.Level));
    }

    [Fact]
    public void The_full_screen_band_retracts_toward_the_edges_and_never_reaches_the_middle()
    {
        var t = LightsDownTuning.Stock;
        var full = LightsDownFullScreen.Look(1, t, LightsDownMotion.Full, H);
        var half = LightsDownFullScreen.Look(0.5, t, LightsDownMotion.Full, H);
        var little = LightsDownFullScreen.Look(0.1, t, LightsDownMotion.Full, H);
        Assert.True(full.Front > half.Front && half.Front > little.Front, $"{full.Front} {half.Front} {little.Front}");
        Assert.True(full.Alpha > half.Alpha && half.Alpha > little.Alpha);
        Assert.False(LightsDownFullScreen.Look(0, t, LightsDownMotion.Full, H).Visible);
        // Small: the band never reaches a fifth of the short side, the middle of the picture stays clean.
        Assert.True(full.Front <= 0.2 * H);
        double centre = LightsDownCanvas.EdgeDepth(W / 2, H / 2, W, H);
        Assert.Equal(0, LightsDownCanvas.Vignette(centre, full.Front, full.Feather), 9);
        // At the very edge it is near full strength.
        Assert.True(LightsDownCanvas.Vignette(LightsDownCanvas.EdgeDepth(0, H / 2, W, H), full.Front, full.Feather) > 0.9);
    }

    [Fact]
    public void Motion_off_only_fades_and_reduced_travels_half()
    {
        var t = LightsDownTuning.Stock;
        var offFull = LightsDownFullScreen.Look(1, t, LightsDownMotion.Off, H);
        var offLow = LightsDownFullScreen.Look(0.3, t, LightsDownMotion.Off, H);
        Assert.Equal(offFull.Front, offLow.Front, 9);
        Assert.True(offLow.Alpha < offFull.Alpha);

        var full = LightsDownFullScreen.Look(0.3, t, LightsDownMotion.Full, H);
        var reduced = LightsDownFullScreen.Look(0.3, t, LightsDownMotion.Reduced, H);
        Assert.True(full.Front < reduced.Front && reduced.Front < offLow.Front);
    }

    // ---------------- floating: vignette growth ----------------

    [Fact]
    public void The_floating_dim_grows_from_the_edges_until_the_whole_screen_is_under_it()
    {
        var t = LightsDownTuning.Stock;
        double centre = LightsDownCanvas.EdgeDepth(W / 2, H / 2, W, H);
        double edge = LightsDownCanvas.EdgeDepth(4, H / 2, W, H);
        double Shade(double grow, double depth)
        {
            var look = LightsDownFloating.Look(grow, 1, t, LightsDownMotion.Full, H, 0);
            return look.Alpha * LightsDownCanvas.Vignette(depth, look.Front, look.Feather);
        }
        Assert.Equal(0, Shade(0, edge), 9);
        Assert.True(Shade(0.3, edge) > 0.05, "early on the edges darken");
        Assert.Equal(0, Shade(0.3, centre), 9);
        Assert.True(Shade(0.7, centre) > 0 && Shade(0.7, centre) < Shade(0.7, edge), "it walks inward");
        Assert.Equal(t.PeakAlpha, Shade(1, centre), 9);
        Assert.Equal(t.PeakAlpha, Shade(1, edge), 9);
        double last = -1;
        for (double g = 0; g <= 1.0001; g += 0.05)
        {
            double v = Shade(g, centre * 0.5);
            Assert.True(v >= last - 1e-12, "growth never steps back");
            last = v;
        }
    }

    [Fact]
    public void The_floating_growth_takes_its_seconds_and_dim_speed_shortens_it()
    {
        var stock = new LightsDownFloating();
        for (double x = 0; x < LightsDownOptions.StockGrowSeconds / 2; x += Dt) stock.Step(Dt, LightsDownTuning.Stock, 0);
        Assert.InRange(stock.Grow, 0.47, 0.53);
        var fast = new LightsDownFloating();
        var ft = LightsDownOptions.Tuning(50, 150, 100);
        for (double x = 0; x < LightsDownOptions.StockGrowSeconds / 2; x += Dt) fast.Step(Dt, ft, 0);
        Assert.True(fast.Grow > stock.Grow);
        for (int i = 0; i < 2000; i++) stock.Step(Dt, LightsDownTuning.Stock, 0);
        Assert.Equal(1, stock.Grow, 9);
    }

    [Fact]
    public void The_floating_dim_fades_out_at_the_end_and_gentle_makes_it_longer()
    {
        LightsDownFloating Ended(double gentle, double seconds)
        {
            var f = new LightsDownFloating();
            for (int i = 0; i < 300; i++) f.Step(Dt, LightsDownTuning.Stock, gentle);
            f.BeginEnd();
            for (double x = 0; x < seconds; x += Dt) f.Step(Dt, LightsDownTuning.Stock, gentle);
            return f;
        }
        var mid = Ended(0, LightsDownFloating.EndSeconds / 2);
        Assert.InRange(mid.Fade, 0.35, 0.65);
        Assert.False(mid.Done);
        Assert.True(Ended(0, LightsDownFloating.EndSeconds + 0.1).Done);
        Assert.False(Ended(1.5, LightsDownFloating.EndSeconds + 0.1).Done, "Gentle never shortens a fade");
        Assert.True(Ended(1.5, 1.6).Done);
        var look = LightsDownFloating.Look(1, 0, LightsDownTuning.Stock, LightsDownMotion.Full, H, 0);
        Assert.False(look.Visible);
    }

    // ---------------- holes and halos ----------------

    private static readonly LightsDownRect Win = new(700, 400, 640, 360);

    [Fact]
    public void A_hole_eases_open_follows_its_window_and_eases_shut_where_it_was()
    {
        var holes = new LightsDownHoles();
        var live = new List<(long, LightsDownRect, bool)> { (1, Win, true) };
        holes.Update(Dt, live, 0);
        Assert.True(holes.Current[0].Presence > 0 && holes.Current[0].Presence < 0.1);
        for (double x = 0; x < LightsDownHoles.InSeconds; x += Dt) holes.Update(Dt, live, 0);
        Assert.Equal(1, holes.Current[0].Presence, 9);
        Assert.False(holes.Changed, "a still window changes nothing");

        var moved = Win with { X = Win.X + 30 };
        live[0] = (1, moved, true);
        holes.Update(Dt, live, 0);
        Assert.True(holes.Changed);
        Assert.Equal(moved, holes.Current[0].Rect);
        Assert.Equal(1, holes.Count);

        live.Clear();
        holes.Update(Dt, live, 0);
        Assert.Equal(moved, holes.Current[0].Rect);
        Assert.True(holes.Current[0].Presence < 1);
        for (double x = 0; x < LightsDownHoles.OutSeconds + 0.05; x += Dt) holes.Update(Dt, live, 0);
        Assert.Empty(holes.Current);
        Assert.Equal(0, holes.Count);
    }

    [Fact]
    public void Several_windows_each_keep_their_own_hole_and_a_target_clears_at_once()
    {
        var holes = new LightsDownHoles();
        var live = new List<(long, LightsDownRect, bool)>();
        for (int k = 0; k < 4; k++) live.Add((k + 1, new LightsDownRect(100 + 400 * k, 200, 300, 170), true));
        live.Add((99, new LightsDownRect(60, 900, 120, 60), false));
        holes.Update(Dt, live, 0);
        Assert.Equal(5, holes.Current.Count);
        foreach (var h in holes.Current)
            if (!h.Halo) Assert.Equal(1, h.Presence, 9);
            else Assert.True(h.Presence < 0.1);

        // One window closes: the others stay open.
        for (int i = 0; i < 30; i++) holes.Update(Dt, live, 0);
        live.RemoveAt(1);
        for (int i = 0; i < 30; i++) holes.Update(Dt, live, 0);
        Assert.Equal(4, holes.Current.Count);
    }

    [Fact]
    public void Gentle_slows_a_hole_opening()
    {
        var a = new LightsDownHoles();
        var b = new LightsDownHoles();
        var live = new List<(long, LightsDownRect, bool)> { (1, Win, true) };
        for (int i = 0; i < 6; i++) { a.Update(Dt, live, 0); b.Update(Dt, live, 1.0); }
        Assert.True(b.Current[0].Presence < a.Current[0].Presence);
    }

    private static (double A, double R, double G, double B) Px(double x, double y, double vignette, params LightsDownHole[] holes)
        => LightsDownCanvas.Shade(x, y, vignette, holes, 0xFF69B4, 1);

    [Fact]
    public void Hole_and_halo_geometry_clear_by_the_window_tint_just_outside_and_dim_further_out()
    {
        var hole = new LightsDownHole(Win, 1, true);
        double v = 0.64;
        var inside = Px(Win.X + Win.W / 2, Win.Y + Win.H / 2, v, hole);
        Assert.True(inside.A < 0.02, $"inside {inside.A}");

        var near = Px(Win.Right + 10, Win.Y + Win.H / 2, v, hole);
        Assert.True(near.A > 0.15, "a halo just outside the edge");
        Assert.True(near.R > near.B && near.R > near.G * 1.5, "the halo wears the accent");

        var far = Px(Win.Right + 300, Win.Y + Win.H / 2, v, hole);
        Assert.Equal(v, far.A, 6);
        Assert.True(far.R < 0.02, "far out it is the plain dim");

        // The halo is quiet: never louder than the cap, whatever the darkness.
        var loud = Px(Win.Right + 4, Win.Y + Win.H / 2, LightsDownOptions.MaxAlpha, hole);
        // The halo tints the dim and never clears it (a clear ring let a bright app shine round the video), so it
        // may sit a little over the dim's own cap, never opaque.
        Assert.True(loud.A <= LightsDownOptions.MaxAlpha + LightsDownCanvas.HaloMax * (1 - LightsDownOptions.MaxAlpha) + 1e-9 && loud.A < 1);
        Assert.True(Px(Win.Right + 2, Win.Y + Win.H / 2, v, hole).A >= v - 1e-9, "no clear ring outside the window");
        Assert.True(loud.R <= LightsDownCanvas.HaloMax + 0.05);

        // No dim, no halo: the halo only lives where the dim reaches.
        Assert.Equal(0, Px(Win.Right + 10, Win.Y + Win.H / 2, 0, hole).A, 9);
    }

    [Fact]
    public void Dim_holes_and_halos_blend_with_no_seam()
    {
        // Walk out from the window edge along a row and along a diagonal past a corner, and between
        // two windows that nearly touch: no step between neighbours bigger than a soft gradient allows.
        var a = new LightsDownHole(Win, 1, true);
        var b = new LightsDownHole(new LightsDownRect(Win.Right + 50, Win.Y + 60, 400, 225), 1, true);
        double v = 0.64;
        void Walk(double x0, double y0, double dx, double dy, int n, params LightsDownHole[] hs)
        {
            var last = Px(x0, y0, v, hs);
            for (int i = 1; i < n; i++)
            {
                var p = Px(x0 + dx * i, y0 + dy * i, v, hs);
                Assert.True(Math.Abs(p.A - last.A) < 0.05, $"alpha jump {last.A:0.000} -> {p.A:0.000} at step {i}");
                Assert.True(Math.Abs(p.R - last.R) < 0.05, $"red jump at step {i}");
                last = p;
            }
        }
        // From the window edge outward (inside is under the window itself).
        Walk(Win.Right, Win.Y + 100, 2, 0, 200, a);
        Walk(Win.Right - 3, Win.Bottom - 3, 1.5, 1.5, 200, a);
        Walk(Win.X, Win.Y - 1, 0, -2, 150, a);
        Walk(Win.Right, Win.Y + 30, 2, 0, 25, a, b);
        Walk(Win.X + 100, Win.Bottom, 3, 1, 150, a, b);
    }

    [Fact]
    public void Rounded_rect_distance_is_signed()
    {
        Assert.True(LightsDownCanvas.RoundedRectDistance(Win.X + 10, Win.Y + 10, Win, 10) < 0);
        Assert.Equal(25, LightsDownCanvas.RoundedRectDistance(Win.Right + 25, Win.Y + 100, Win, 10), 9);
        Assert.Equal(-5, LightsDownCanvas.RoundedRectDistance(Win.Right - 5, Win.Y + 100, Win, 10), 9);
        Assert.True(LightsDownCanvas.EdgeDepth(0, 0, W, H) < 0, "the rounded corner sits outside, so it dims first");
    }

    [Fact]
    public void A_frame_only_repaints_when_it_changed_visibly()
    {
        var a = new LightsDownLook(0.5, 100, 100, 0);
        Assert.False(LightsDownCanvas.Worth(a, a, false));
        Assert.False(LightsDownCanvas.Worth(a, a with { Alpha = 0.501 }, false));
        Assert.True(LightsDownCanvas.Worth(a, a with { Alpha = 0.51 }, false));
        Assert.True(LightsDownCanvas.Worth(a, a with { Front = 103 }, false));
        Assert.True(LightsDownCanvas.Worth(a, a, true));
        Assert.True(LightsDownCanvas.Worth(a, LightsDownLook.None, false));
        Assert.False(LightsDownCanvas.Worth(LightsDownLook.None, LightsDownLook.None, false));
    }

    [Fact]
    public void The_small_picture_stays_small_on_every_screen()
    {
        foreach (var (w, h) in new[] { (1280.0, 720.0), (1920.0, 1080.0), (2560.0, 1440.0), (3840.0, 2160.0), (5120.0, 1440.0) })
        {
            var c = new LightsDownCanvas(w, h, 1);
            Assert.True(c.Width <= LightsDownCanvas.TargetCells + 1, $"{w}: {c.Width}");
            Assert.True(c.Width * c.Cell >= w && c.Height * c.Cell >= h);
        }
    }

    [Fact]
    public void Drawing_with_four_windows_is_cheap()
    {
        var c = new LightsDownCanvas(2560, 1440, 1);
        var holes = new List<LightsDownHole>();
        for (int k = 0; k < 4; k++) holes.Add(new LightsDownHole(new LightsDownRect(150 + 560 * k, 300 + 120 * k, 640, 360), 1, true));
        var look = LightsDownFloating.Look(1, 1, LightsDownTuning.Stock, LightsDownMotion.Full, 1440, 0xFF69B4);
        c.Draw(look, holes);   // warm
        // The best of several runs: the full suite runs in parallel and steals the CPU.
        double ms = double.MaxValue;
        for (int run = 0; run < 5; run++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) c.Draw(look, holes);
            ms = Math.Min(ms, sw.Elapsed.TotalMilliseconds / 10);
        }
        Assert.True(ms < 20, $"{ms:0.00} ms a frame");   // about 3 ms in Debug at 2560x1440; 8.6 under the whole suite's load
    }

    // ---------------- rules ----------------

    [Fact]
    public void Mouse_moves_count_only_past_a_tremor()
    {
        Assert.False(LightsDownRules.Moved(double.NaN, double.NaN, 10, 10));
        Assert.False(LightsDownRules.Moved(100, 100, 102, 101));
        Assert.True(LightsDownRules.Moved(100, 100, 104, 100));
    }

    [Theory]
    [InlineData(false, true, true, false, false)]   // camera off: never, whatever the stale state says
    [InlineData(true, false, false, false, false)]  // running, face there, no calibrated gaze: mouse only
    [InlineData(true, true, false, false, true)]    // the face left
    [InlineData(true, false, true, true, false)]    // gaze on the video
    [InlineData(true, false, true, false, true)]    // gaze off every video screen
    public void Looking_away_needs_the_camera_already_running(bool running, bool faceLost, bool gazeLive, bool onVideo, bool away)
        => Assert.Equal(away, LightsDownRules.LookedAway(running, faceLost, gazeLive, onVideo));

    [Fact]
    public void It_never_runs_past_the_video()
    {
        Assert.True(LightsDownRules.ShouldRun(true, true, false, true));
        Assert.False(LightsDownRules.ShouldRun(false, true, false, true));
        Assert.False(LightsDownRules.ShouldRun(true, false, false, true));
        Assert.False(LightsDownRules.ShouldRun(true, true, true, true));
        Assert.False(LightsDownRules.ShouldRun(true, true, false, false));
    }

    // ---------------- the attention mini game and floating ----------------

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void The_mini_game_reads_as_off_while_floating_is_the_pick(bool setting, bool floating, bool shown)
    {
        Assert.Equal(shown, MandatoryVideoPresentation.MiniGameShownOn(setting, floating));
        Assert.Equal(!floating, MandatoryVideoPresentation.MiniGameEditable(floating));
        // The runtime gate agrees for a video that floats, and a full-screen one keeps the setting.
        Assert.Equal(setting && !floating, MandatoryVideoPresentation.AttentionChecksApply(setting, floating));
    }

    [Fact]
    public void Floating_never_writes_the_saved_mini_game_setting()
    {
        var s = new AppSettings { AttentionChecksEnabled = true, MandatoryVideoFloating = true };
        Assert.False(MandatoryVideoPresentation.MiniGameShownOn(s.AttentionChecksEnabled, s.MandatoryVideoFloating));
        s.MandatoryVideoFloating = false;
        Assert.True(s.AttentionChecksEnabled);
        Assert.True(MandatoryVideoPresentation.MiniGameShownOn(s.AttentionChecksEnabled, s.MandatoryVideoFloating));
    }
}
