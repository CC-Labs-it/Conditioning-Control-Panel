using System;
using System.Diagnostics;
using System.IO;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The fog moves: a moving cursor shoves it (it is carried, not rubbed out), the pool leaves it
/// turning, a pop throws a puff, and it always billows a little. Also the cost of a step.
/// </summary>
public class SteamMotionTests
{
    private const double Dt = 1 / 30.0;

    private static SteamField Full()
    {
        var f = new SteamField(1920, 1080);
        f.Settle(1.5, 1);
        return f;
    }

    private static double Mass(SteamField f)
    {
        double s = 0;
        foreach (float v in f.Fog) s += v;
        return s;
    }

    /// <summary>Fog above its resting level in a box (pixels), as a sum over cells.</summary>
    private static double Excess(SteamField f, double x0, double y0, double x1, double y1)
    {
        double s = 0;
        for (int gy = (int)(y0 / 1080 * f.H); gy <= (int)(y1 / 1080 * f.H) && gy < f.H; gy++)
            for (int gx = (int)(x0 / 1920 * f.W); gx <= (int)(x1 / 1920 * f.W) && gx < f.W; gx++)
                s += Math.Max(0, f.Fog[gy * f.W + gx] - 1);
        return s;
    }

    private static void SweepRight(SteamField f, double fromX, double toX, double y, double pxPerSecond, bool step = true)
    {
        double x = fromX;
        while (x < toX)
        {
            double nx = Math.Min(toX, x + pxPerSecond * Dt);
            f.Push(x, y, nx, y, 58, Dt, mark: true);
            if (step) f.Step(Dt, 1.5, 1);
            x = nx;
        }
    }

    [Fact]
    public void A_moving_cursor_shoves_the_fog_ahead_and_aside_instead_of_erasing_it()
    {
        var f = Full();
        SweepRight(f, 500, 1000, 540, 900);
        // The path is clear behind the cursor.
        Assert.True(f.At(800, 540) < 0.6, $"path {f.At(800, 540)}");
        // The fog went somewhere: more ahead of where the cursor stopped than behind where it began.
        double ahead = Excess(f, 1000, 380, 1300, 700), behind = Excess(f, 200, 380, 500, 700);
        Assert.True(ahead > 2, $"ahead {ahead}");
        Assert.True(ahead > behind * 3, $"ahead {ahead} behind {behind}");
        // And it banked up along both sides of the path.
        Assert.True(Excess(f, 600, 380, 900, 500) > 1, "upper bank");
        Assert.True(Excess(f, 600, 580, 900, 700) > 1, "lower bank");
        // The fog there is left travelling the way the cursor went.
        int cell = (int)(540 / 1080.0 * f.H) * f.W + (int)(1000 / 1920.0 * f.W);
        Assert.True(f.Vx[cell] > 3, $"vx {f.Vx[cell]}");
        Assert.True(Math.Abs(f.Vy[cell]) < f.Vx[cell]);
    }

    [Fact]
    public void A_fast_push_keeps_most_of_the_fog_and_a_plain_wipe_does_not()
    {
        var pushed = Full(); var wiped = Full();
        double before = Mass(pushed);
        SweepRight(pushed, 500, 1400, 540, 1500, step: false);
        for (double x = 500; x <= 1400; x += 1500 * Dt) wiped.Wipe(x, 540, 58, Dt, mark: true);
        double lostPush = before - Mass(pushed), lostWipe = before - Mass(wiped);
        Assert.True(lostWipe > 5, $"wipe lost {lostWipe}");
        Assert.True(lostPush < lostWipe * 0.35, $"push lost {lostPush}, wipe lost {lostWipe}");
        // Carrying the fog on its own neither makes nor loses any (the heal aside).
        double m0 = Mass(pushed);
        var probe = new SteamField(1920, 1080);
        Array.Copy(pushed.Fog, probe.Fog, probe.Fog.Length);
        probe.Push(900, 300, 960, 300, 58, Dt, false);
        double m1 = Mass(probe);
        probe.Step(1e-9, 1.5, 1);   // no time: nothing moves
        Assert.Equal(m1, Mass(probe), 3);
        Assert.True(Math.Abs(m1 - m0) < m0 * 0.01);
    }

    [Fact]
    public void A_faster_cursor_shoves_harder_and_a_still_one_only_clears_its_spot()
    {
        var slow = Full(); var fast = Full(); var still = Full();
        SweepRight(slow, 500, 900, 540, 150, step: false);
        SweepRight(fast, 500, 900, 540, 1500, step: false);
        double Speed(SteamField f) { double m = 0; for (int i = 0; i < f.Vx.Length; i++) m = Math.Max(m, Math.Abs(f.Vx[i])); return m; }
        Assert.True(Speed(fast) > Speed(slow) * 2, $"fast {Speed(fast)} slow {Speed(slow)}");
        Assert.True(Speed(fast) <= SteamField.MaxSpeed + 0.01);

        for (int i = 0; i < 30; i++) still.Push(700, 400, 700, 400, 58, Dt, mark: true);
        Assert.True(still.At(700, 400) < 0.1);
        Assert.Equal(0, Speed(still), 3);
        Assert.Equal(0, Excess(still, 0, 0, 1919, 1079), 3);
    }

    [Fact]
    public void Pushed_fog_coasts_to_a_stop_and_the_path_heals()
    {
        var f = Full();
        SweepRight(f, 500, 1000, 540, 900);
        double Energy() { double e = 0; for (int i = 0; i < f.Vx.Length; i++) e += f.Vx[i] * f.Vx[i] + f.Vy[i] * f.Vy[i]; return e; }
        double e0 = Energy();
        Assert.True(e0 > 100);
        for (int i = 0; i < 30; i++) f.Step(Dt, 1.5, 1);
        double e1 = Energy();
        Assert.InRange(e1, e0 * 0.02, e0 * 0.8);            // one second on: still drifting, slower
        for (int i = 0; i < 30 * 9; i++) f.Step(Dt, 1.5, 1);
        Assert.True(Energy() < e0 * 0.001);                 // ten seconds on: at rest
        for (int i = 0; i < 30 * 20; i++) f.Step(Dt, 1.5, 1);
        Assert.InRange(f.At(800, 540), 0.93, 1.07);
        Assert.InRange(f.At(1100, 540), 0.93, 1.07);
    }

    [Fact]
    public void The_pool_leaves_the_fog_turning_and_it_runs_down()
    {
        var f = Full();
        double angle = 0;
        for (int i = 0; i < 90; i++)
        {
            angle += 2.8 * Dt;
            f.Step(Dt, 1.5, 1);
            f.Swirl(960, 540, 420, 1, angle, 2.8, 2.1, Dt, 1);
        }
        // Right of the centre the fog moves along the turn (up or down), not in or out.
        int cx = (int)(960 / 1920.0 * f.W), cy = (int)(540 / 1080.0 * f.H), right = cy * f.W + cx + 15, below = (cy + 15) * f.W + cx;
        double v0 = Math.Abs(f.Vy[right]);
        Assert.True(v0 > 4, $"turning at {v0}");
        Assert.True(Math.Abs(f.Vx[right]) < v0 * 0.3);
        // A turn, not a blow: the two sides run opposite ways.
        Assert.True(f.Vy[right] * f.Vy[cy * f.W + cx - 15] < 0);
        Assert.True(Math.Abs(f.Vx[below]) > 4);
        // The pool is gone. A second later the patch is still turning.
        for (int i = 0; i < 30; i++) f.Step(Dt, 1.5, 1);
        double v1 = Math.Abs(f.Vy[right]);
        Assert.InRange(v1, v0 * 0.25, v0 * 0.9);
        for (int i = 0; i < 30 * 9; i++) f.Step(Dt, 1.5, 1);
        Assert.True(Math.Abs(f.Vy[right]) < v0 * 0.02, "ten seconds on it has stopped");
    }

    [Fact]
    public void Momentum_carries_the_fog_after_the_pool_leaves()
    {
        // A patch wound in the open (fog only on one side) keeps being carried round once the
        // pool is gone: the fog front moves on without anything pushing it.
        var f = new SteamField(1920, 1080);
        for (int gy = 0; gy < f.H; gy++) for (int gx = 0; gx < f.W / 2; gx++) f.Fog[gy * f.W + gx] = 1;
        for (int i = 0; i < 20; i++) f.Swirl(960, 540, 300, 1, 0, 2.8, 2.1, Dt, 1, arms: 0);
        var still = new SteamField(1920, 1080);
        Array.Copy(f.Fog, still.Fog, f.Fog.Length);
        for (int i = 0; i < 45; i++) { f.Step(Dt, 0, 1); still.Step(Dt, 0, 1); }
        double diff = 0;
        for (int i = 0; i < f.Fog.Length; i++) diff += Math.Abs(f.Fog[i] - still.Fog[i]);
        Assert.True(diff > 20, $"carried {diff}");
    }

    [Fact]
    public void A_pop_throws_a_puff_outward()
    {
        var f = Full();
        double before = Mass(f);
        f.Hole(960, 540, 150);
        Assert.True(f.At(960, 540) < 0.05);
        Assert.True(f.At(960 + 210, 540) > 1.02, $"bank {f.At(960 + 210, 540)}");
        Assert.True(Mass(f) < before);
        int cy = (int)(540 / 1080.0 * f.H), cx = (int)(960 / 1920.0 * f.W);
        Assert.True(f.Vx[cy * f.W + cx + 13] > 5);
        Assert.True(f.Vx[cy * f.W + cx - 13] < -5);
        Assert.True(f.Vy[(cy + 13) * f.W + cx] > 5);
    }

    [Fact]
    public void Fog_at_rest_still_billows_but_stays_even_and_never_strobes()
    {
        var f = Full();
        float[] prev = (float[])f.Fog.Clone();
        double worstJump = 0;
        for (int i = 0; i < 30 * 30; i++)
        {
            f.Step(Dt, 1.5, 1);
            for (int k = 0; k < prev.Length; k += 7) worstJump = Math.Max(worstJump, Math.Abs(f.Fog[k] - prev[k]));
            Array.Copy(f.Fog, prev, prev.Length);
        }
        Assert.True(worstJump < 0.02, $"jump {worstJump}");
        foreach (float v in f.Fog) Assert.InRange(v, 0.9f, 1.1f);
        Assert.True(f.Dirty);

        // A creeping front (fog only round the edges) is moved by the billow.
        var front = new SteamField(1920, 1080);
        front.Settle(0.6, 1);
        float[] start = (float[])front.Fog.Clone();
        for (int i = 0; i < 30 * 20; i++) front.Step(Dt, 0.6, 1);
        double moved = 0;
        for (int i = 0; i < start.Length; i++) moved = Math.Max(moved, Math.Abs(front.Fog[i] - start[i]));
        Assert.InRange(moved, 0.03, 0.6);
        // No fog blows in through an edge or piles against it.
        for (int gx = 0; gx < front.W; gx++) Assert.InRange(front.Fog[gx], 0.85f, 1.15f);
    }

    [Fact]
    public void An_empty_monitor_is_not_dirty()
    {
        var f = new SteamField(1920, 1080);
        f.ClearDirty();
        f.Step(Dt, 0, 1);
        Assert.False(f.Dirty);
    }

    [Fact]
    public void The_stage_passes_a_stroke_to_the_right_monitor()
    {
        var s = new SuperStage { SteamOn = true };
        var left = s.Field("l", -1920, 0, 1920, 1080);
        var right = s.Field("r", 0, 0, 1920, 1080);
        left.Settle(1.5, 1); right.Settle(1.5, 1);
        for (int i = 0; i < 10; i++) s.Push(-1200 + i * 30, 500, -1170 + i * 30, 500, 58, Dt, mark: true);
        Assert.True(left.At(1920 - 1000, 500) < 0.9);
        double v = 0;
        foreach (float x in left.Vx) v = Math.Max(v, x);
        Assert.True(v > 3);
        foreach (float x in right.Vx) Assert.Equal(0f, x);
        Assert.Equal(1, right.At(960, 500), 3);
        s.SteamOn = false;
        double m = Mass(left);
        s.Push(-900, 500, -800, 500, 58, Dt, true);
        Assert.Equal(m, Mass(left), 6);
    }

    [Fact]
    public void A_step_is_cheap()
    {
        var f = Full();
        double angle = 0;
        void Busy(int i)
        {
            // Worst case on purpose: the cursor racing about, a pool winding, pops going off.
            double x = 960 + Math.Cos(i * 0.21) * 700, y = 540 + Math.Sin(i * 0.17) * 380;
            double px = 960 + Math.Cos((i - 1) * 0.21) * 700, py = 540 + Math.Sin((i - 1) * 0.17) * 380;
            f.Push(px, py, x, y, 58, Dt, true);
            if (i % 20 == 0) f.Hole(300 + i % 900, 300, 120);
        }
        for (int i = 1; i < 60; i++) { Busy(i); f.Step(Dt, 1.5, 1); }   // warm the JIT
        var sw = new Stopwatch();
        const int n = 600;
        long stepTicks = 0, allTicks = 0, batch = 0, bestBatch = long.MaxValue;
        for (int i = 1; i <= n; i++)
        {
            long a = Stopwatch.GetTimestamp();
            Busy(i);
            long b = Stopwatch.GetTimestamp();
            f.Step(Dt, 1.5, 1);
            long c = Stopwatch.GetTimestamp();
            angle += 2.8 * Dt;
            f.Swirl(1300, 600, 420, 1, angle, 2.8, 2.1, Dt, 1);
            long d = Stopwatch.GetTimestamp();
            stepTicks += c - b; allTicks += d - a; batch += c - b;
            // The bound is judged on the quietest fifty steps, so other tests hogging the CPU do not fail it.
            if (i % 50 == 0) { bestBatch = Math.Min(bestBatch, batch); batch = 0; }
        }
        double stepMs = stepTicks * 1000.0 / Stopwatch.Frequency / n, allMs = allTicks * 1000.0 / Stopwatch.Frequency / n;
        // And the usual case: fog at rest, only the billow moving it.
        var calm = Full();
        for (int i = 0; i < 400; i++) calm.Step(Dt, 1.5, 1);
        long c0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++) calm.Step(Dt, 1.5, 1);
        double calmMs = (Stopwatch.GetTimestamp() - c0) * 1000.0 / Stopwatch.Frequency / n;
        var note = Environment.GetEnvironmentVariable("CCP_STEAM_DUMP");
        if (!string.IsNullOrEmpty(note))
        {
            Directory.CreateDirectory(note);
            File.WriteAllText(Path.Combine(note, "step-cost.txt"), $"Busy step {stepMs.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} ms, whole frame (push + step + swirl) {allMs.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} ms, step at rest {calmMs.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} ms, grid {f.W}x{f.H}\n");
        }
        // The aim is well under 1 ms; the bound is loose so a loaded CI box does not flake.
        double bestMs = bestBatch * 1000.0 / Stopwatch.Frequency / 50;
        Assert.True(bestMs < 3, $"step {bestMs} ms at best, {stepMs} ms on average");
    }

    /// <summary>
    /// Proof in pictures, only when CCP_STEAM_DUMP names a folder: the cursor sweeping through
    /// settled fog, then a pool spinning and leaving, drawn with the real SteamDraw.
    /// </summary>
    [Fact]
    public void Dump_frames_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_STEAM_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        const int w = 1920, h = 1080;
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var draw = new SteamDraw();
        double t = 0;
        var drawTime = new Stopwatch(); int shots = 0;
        void Shot(SteamField f, string name)
        {
            // A plain desktop stand-in: dark, with a few light blocks so the fog's thickness reads.
            canvas.Clear(new SKColor(24, 28, 40));
            using var p = new SKPaint { Color = new SKColor(70, 82, 110) };
            for (int k = 0; k < 6; k++) canvas.DrawRect(120 + k * 300, 200 + (k % 2) * 380, 220, 260, p);
            drawTime.Start();
            draw.Draw(canvas, new SKRect(0, 0, w, h), f, new SKColor(255, 105, 180), 0.62, Array.Empty<string>(), null, SKTypeface.Default, t);
            canvas.Flush();
            drawTime.Stop(); shots++;
            using var half = bmp.Resize(new SKImageInfo(w / 2, h / 2), SKFilterQuality.Medium);
            using var data = half.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), data.ToArray());
        }
        void Run(SteamField f, double seconds, Action<int>? each = null)
        {
            int n = (int)Math.Round(seconds * 30);
            for (int i = 0; i < n; i++) { each?.Invoke(i); f.Step(Dt, 1.5, 1); t += Dt; }
        }

        // 1. At rest: the texture alone, two shots three seconds apart.
        var rest = Full();
        Run(rest, 2); Shot(rest, "rest-0s");
        Run(rest, 3); Shot(rest, "rest-3s");
        var front = new SteamField(w, h);
        front.Settle(0.7, 1);
        for (int i = 0; i < 30 * 15; i++) { front.Step(Dt, 0.7, 1); t += Dt; }
        Shot(front, "front-15s");

        // 2. The cursor sweeps across in an arc at about 1100 px/s.
        var f = Full();
        Run(f, 1);
        double X(double s) => 300 + s * 1100;
        double Y(double s) => 620 - Math.Sin(s * 2.6) * 260;
        double s0 = 0;
        for (int k = 0; k < 4; k++)
        {
            Run(f, 0.3, i => { double a = s0, b = s0 + Dt; f.Push(X(a), Y(a), X(b), Y(b), 58, Dt, true); s0 = b; });
            Shot(f, $"sweep-{k + 1}-moving");
        }
        Run(f, 0.5); Shot(f, "sweep-5-after-0.5s");
        Run(f, 1.0); Shot(f, "sweep-6-after-1.5s");
        Run(f, 2.5); Shot(f, "sweep-7-after-4s");

        // 3. A slow drag and a still cursor.
        var g = Full();
        Run(g, 1);
        double gx = 400;
        Run(g, 2.0, i => { g.Push(gx, 500, gx + 6, 500, 58, Dt, true); gx += 6; });
        Shot(g, "slow-drag");
        Run(g, 1.5, i => g.Push(gx, 500, gx, 500, 58, Dt, true));
        Shot(g, "still-cursor");

        // 4. A pool spins up, holds, then leaves; a pop goes off beside it.
        var stage = new SuperStage { SteamOn = true, PoolOn = true };
        var pf = stage.Field("a", 0, 0, w, h);
        pf.Settle(1.5, 1);
        for (int i = 0; i < 30 * 8; i++) { stage.Pool.Step(Dt, 1000, 540, 0, 1, h); stage.StepSteam(Dt, 1.5, 1); t += Dt; }
        Shot(pf, "pool-1-spinning");
        stage.PoolOn = false;
        for (int k = 0; k < 4; k++)
        {
            for (int i = 0; i < 15; i++) { stage.StepSteam(Dt, 1.5, 1); t += Dt; }
            Shot(pf, $"pool-{k + 2}-left-{(k + 1) * 5}");
        }
        for (int i = 0; i < 60; i++) { stage.StepSteam(Dt, 1.5, 1); t += Dt; }
        Shot(pf, "pool-6-left-4s");
        stage.Blast(new Blast(500, 400, 300, 140, BlastKind.BubblePop));
        Shot(pf, "pop-0s");
        for (int i = 0; i < 12; i++) { stage.StepSteam(Dt, 1.5, 1); t += Dt; }
        Shot(pf, "pop-0.4s");
        for (int i = 0; i < 30; i++) { stage.StepSteam(Dt, 1.5, 1); t += Dt; }
        Shot(pf, "pop-1.4s");
        File.WriteAllText(Path.Combine(dir, "draw-cost.txt"), FormattableString.Invariant($"SteamDraw.Draw on a CPU canvas at {w}x{h}: {drawTime.Elapsed.TotalMilliseconds / shots:F2} ms a frame over {shots} frames"));
    }
}
