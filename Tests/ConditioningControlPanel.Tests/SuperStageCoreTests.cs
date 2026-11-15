using System;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The shared Super stage (CCP.Core/Super): the pool, the fog, the word fling, the bubble merge rules.</summary>
public class SuperStageCoreTests
{
    private static Whirlpool Rested(double seconds, double x = 500, double y = 400)
    {
        var p = new Whirlpool();
        for (double t = 0; t < seconds; t += 1 / 30.0) p.Step(1 / 30.0, x, y, 0, 1, 1080);
        return p;
    }

    [Fact]
    public void Pool_grows_at_rest_and_takes_its_time()
    {
        Assert.False(Rested(0.5).Live);
        var half = Rested(Whirlpool.GrowSeconds / 2);
        Assert.InRange(half.Size, 0.45, 0.55);
        Assert.Equal(1, Rested(Whirlpool.GrowSeconds + 1).Size, 3);
    }

    [Fact]
    public void Pool_shrinks_while_the_mouse_moves_and_slower_than_a_blink()
    {
        var p = Rested(Whirlpool.GrowSeconds + 1);
        for (int i = 0; i < 30; i++) p.Step(1 / 30.0, 500 + i * 30, 400, 900, 1, 1080);
        Assert.InRange(p.Size, 0.5, 0.95);   // one second of fast movement does not kill a full pool
        for (int i = 0; i < 30 * 12; i++) p.Step(1 / 30.0, 500 + i * 30, 400, 900, 1, 1080);
        Assert.Equal(0, p.Size, 3);
    }

    [Fact]
    public void Pool_trails_the_cursor()
    {
        var p = Rested(2, 100, 100);
        p.Step(1 / 30.0, 900, 100, 0, 1, 1080);
        Assert.InRange(p.X, 100, 400);
    }

    [Fact]
    public void Pull_points_inward_shrinks_things_and_eats_at_the_eye()
    {
        var p = Rested(Whirlpool.GrowSeconds + 1);
        var far = p.Pull(p.X + p.Radius * 2, p.Y);
        Assert.False(far.Inside);
        Assert.Equal(1, far.Scale);
        var mid = p.Pull(p.X + p.Radius * 0.4, p.Y);
        Assert.True(mid.Inside);
        Assert.True(mid.Ax < 0);            // toward the pool
        Assert.True(Math.Abs(mid.Ay) > 0);  // and around it
        Assert.InRange(mid.Scale, 0.14, 0.99);
        Assert.False(mid.Eaten);
        Assert.True(p.Pull(p.X + 3, p.Y).Eaten);
        var heavy = p.Pull(p.X + p.Radius * 0.4, p.Y, mass: 4);
        Assert.True(Math.Abs(heavy.Ax) < Math.Abs(mid.Ax));
    }

    [Fact]
    public void A_small_pool_pulls_and_eats_nothing()
    {
        var p = Rested(0.3);
        var r = p.Pull(p.X + 2, p.Y);
        Assert.False(r.Inside);
        Assert.False(r.Eaten);
    }

    [Fact]
    public void Stage_pool_is_dead_while_its_star_is_off()
    {
        var s = new SuperStage();
        for (double t = 0; t < 20; t += 1 / 30.0) s.Pool.Step(1 / 30.0, 500, 400, 0, 1, 1080);
        Assert.False(s.PoolLive);
        Assert.False(s.Pull(505, 400).Inside);
        s.PoolOn = true;
        Assert.True(s.Pull(505, 400).Inside);
    }

    [Fact]
    public void Fog_creeps_from_the_edges_first()
    {
        var f = new SteamField(1920, 1080);
        f.Settle(0.5, 1);
        Assert.True(f.At(10, 540) > 0.8);
        Assert.Equal(0, f.At(960, 540), 2);
        f.Settle(1.5, 1);
        Assert.True(f.At(960, 540) > 0.8);
    }

    [Fact]
    public void A_hole_is_soft_and_heals_slowly()
    {
        var f = new SteamField(1920, 1080);
        f.Settle(1.5, 1);
        f.Hole(960, 540, 150);
        Assert.True(f.At(960, 540) < 0.05);
        Assert.InRange(f.At(960 + 110, 540), 0.2, 0.99);   // faded rim, not a cut
        Assert.True(f.At(960 + 300, 540) > 0.95);
        for (int i = 0; i < 30; i++) f.Step(1 / 30.0, 1.5, 1);
        Assert.InRange(f.At(960, 540), 0.1, 0.4);          // one second in: still clearly a hole
        for (int i = 0; i < 30 * 20; i++) f.Step(1 / 30.0, 1.5, 1);
        Assert.True(f.At(960, 540) > 0.95);
    }

    [Fact]
    public void A_wipe_clears_and_remembers_where()
    {
        var f = new SteamField(1920, 1080);
        f.Settle(1.5, 1);
        for (int i = 0; i < 20; i++) f.Wipe(400, 300, 80, 1 / 30.0, mark: true);
        Assert.True(f.At(400, 300) < 0.1);
        int cell = (int)(300 / 1080.0 * f.H) * f.W + (int)(400 / 1920.0 * f.W);
        Assert.Equal(1f, f.Mem[cell]);
        Assert.Equal(0f, f.Mem[0]);
    }

    [Fact]
    public void The_pool_winds_arms_into_even_fog_and_they_outlast_it()
    {
        var f = new SteamField(1920, 1080);
        f.Settle(1.5, 1);
        double Spread()
        {
            double lo = 1, hi = 0;
            for (int a = 0; a < 48; a++)
            {
                double v = f.At(960 + Math.Cos(a * Math.PI / 24) * 180, 540 + Math.Sin(a * Math.PI / 24) * 180);
                lo = Math.Min(lo, v); hi = Math.Max(hi, v);
            }
            return hi - lo;
        }
        Assert.True(Spread() < 0.01);
        double angle = 0;
        for (int i = 0; i < 90; i++)
        {
            angle += 2.8 / 30;
            f.Step(1 / 30.0, 1.5, 1);
            f.Swirl(960, 540, 420, 1, angle, 2.8, 2.1, 1 / 30.0, 1);
        }
        double wound = Spread();
        Assert.True(wound > 0.15, $"arms spread {wound}");
        Assert.True(f.At(960, 540) < 0.2);                 // the eye is clear
        for (int i = 0; i < 60; i++) f.Step(1 / 30.0, 1.5, 1);
        Assert.True(Spread() > wound * 0.4);               // two seconds after the pool left: still there
        for (int i = 0; i < 30 * 30; i++) f.Step(1 / 30.0, 1.5, 1);
        Assert.True(Spread() < 0.02);                      // and gone in the end
    }

    [Fact]
    public void A_blast_holes_the_fog_and_reaches_every_listener()
    {
        var s = new SuperStage { SteamOn = true };
        var f = s.Field("a", 0, 0, 1920, 1080);
        f.Settle(1.5, 1);
        Blast? heard = null;
        s.Blasted += b => heard = b;
        s.Blast(new Blast(960, 540, 300, 120, BlastKind.RipePop));
        Assert.Equal(BlastKind.RipePop, heard?.Kind);
        Assert.True(f.At(960, 540) < 0.05);
    }

    [Fact]
    public void Fields_are_per_monitor_and_use_world_coordinates()
    {
        var s = new SuperStage { SteamOn = true };
        var left = s.Field("l", -1920, 0, 1920, 1080);
        var right = s.Field("r", 0, 0, 1920, 1080);
        left.Settle(1.5, 1); right.Settle(1.5, 1);
        s.Blast(new Blast(-960, 540, 0, 120, BlastKind.BubblePop));
        Assert.True(left.At(960, 540) < 0.05);
        Assert.True(right.At(960, 540) > 0.95);
        Assert.Same(left, s.Field("l", -1920, 0, 1920, 1080));
        Assert.NotSame(left, s.Field("l", -1920, 0, 2560, 1440));
    }

    [Fact]
    public void Merged_bubbles_add_area_and_pay_the_sum()
    {
        Assert.Equal(Math.Sqrt(2) * 20, FrothRules.MergedRadius(20, 20, 20), 6);
        Assert.Equal(20 * FrothRules.MaxGrow, FrothRules.MergedRadius(80, 80, 20), 6);
        Assert.Equal(7, FrothRules.MergedPayout(3, 4));
        var c = FrothRules.MergedCentre(0, 0, 10, 100, 0, 30);
        Assert.Equal(90, c.X, 6);
        Assert.True(FrothRules.Touching(0, 0, 20, 30, 0, 20));
        Assert.False(FrothRules.Touching(0, 0, 20, 40, 0, 20));
        Assert.False(FrothRules.Ripe(20, 20));
        Assert.True(FrothRules.Ripe(50, 20));
    }

    [Fact]
    public void Two_pictures_in_one_bubble_circle_each_other()
    {
        var one = FrothRules.PictureSlot(0, 1, 3);
        Assert.Equal((0d, 0d), (one.Dx, one.Dy));
        var a = FrothRules.PictureSlot(0, 2, 1); var b = FrothRules.PictureSlot(1, 2, 1);
        Assert.Equal(-a.Dx, b.Dx, 6);
        Assert.True(a.Size < one.Size);
        var later = FrothRules.PictureSlot(0, 2, 2);
        Assert.NotEqual(a.Dx, later.Dx);
    }

    [Fact]
    public void A_word_near_the_eye_is_caught_turned_and_thrown_out_faster()
    {
        var s = new SuperStage { PoolOn = true };
        for (double t = 0; t < 20; t += 1 / 30.0) s.Pool.Step(1 / 30.0, 500, 400, 0, 1, 1080);
        var st = new FlingState();
        double x = 500 + s.Pool.Radius * 0.2, y = 400, vx = -200, vy = 0;
        bool thrown = false; int frames = 0; double closest = 1e9; bool caught = false;
        while (!thrown && frames++ < 600)
        {
            thrown = TextFling.Step(ref st, s, ref x, ref y, ref vx, ref vy, 200, 1 / 30.0);
            if (st.Phase == 1) caught = true;
            if (st.Phase == 0) { x += vx / 30; y += vy / 30; }
            closest = Math.Min(closest, Math.Sqrt((x - 500) * (x - 500) + (y - 400) * (y - 400)));
        }
        Assert.True(caught);
        Assert.True(thrown);
        Assert.True(closest < s.Pool.Radius * 0.1);
        double speed = Math.Sqrt(vx * vx + vy * vy);
        Assert.Equal(200 * TextFling.ThrowBoost, speed, 3);
        // Thrown away from the eye, immune for a while, and back to cruise speed in the end.
        double outward = (x - 500) * vx + (y - 400) * vy;
        Assert.True(outward > 0);
        Assert.True(st.Immune > 3);
        for (int i = 0; i < 90; i++) TextFling.Step(ref st, s, ref x, ref y, ref vx, ref vy, 200, 1 / 30.0);
        Assert.Equal(0, st.Phase);
        Assert.Equal(200, Math.Sqrt(vx * vx + vy * vy), 3);
    }

    [Fact]
    public void A_caught_word_is_let_go_if_the_pool_dies()
    {
        var s = new SuperStage { PoolOn = true };
        for (double t = 0; t < 20; t += 1 / 30.0) s.Pool.Step(1 / 30.0, 500, 400, 0, 1, 1080);
        var st = new FlingState();
        double x = 500 + s.Pool.Radius * 0.2, y = 400, vx = -200, vy = 0;
        for (int i = 0; i < 5; i++) TextFling.Step(ref st, s, ref x, ref y, ref vx, ref vy, 200, 1 / 30.0);
        Assert.Equal(1, st.Phase);
        s.PoolOn = false;
        Assert.True(TextFling.Step(ref st, s, ref x, ref y, ref vx, ref vy, 200, 1 / 30.0));
        Assert.Equal(2, st.Phase);
    }
}
