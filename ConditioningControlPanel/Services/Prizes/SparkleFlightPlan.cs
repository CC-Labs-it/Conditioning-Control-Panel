using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>One star's path: keyframes in window pixels, its size (x 26 px), spin and timing.</summary>
public sealed record SparkleStarPath(
    double X0, double Y0, double Bx, double By, double Mx, double My, double X1, double Y1,
    double Size, double Spin, double DelayMs, double DurMs, bool Leaving);

/// <summary>Where a star is at one moment.</summary>
public readonly record struct SparkleStarPose(double X, double Y, double Scale, double Angle, double Alpha);

/// <summary>
/// THE FLIGHT, as numbers (the star flight of the owner's prize counter mockup v4, functions
/// <c>fly</c> and <c>flyOut</c>). Pure: the caller passes the dice, so a seed is a performance.
/// A gain puffs 10 to 30 stars out of the spot, mostly upward, then streams them to the wallet
/// over about 1.2 to 2.7 s; a spend sends 10 to 22 out of the wallet, and they fade on the way.
/// Motion Off flies nothing, Reduced flies half.
/// </summary>
public static class SparkleFlightPlan
{
    /// <summary>Bell files on disk (Scripts/render-sparkle-bells.mjs): A major pentatonic from A5.</summary>
    public const int Bells = 8;

    /// <summary>Never more than about 16 landing bells a second.</summary>
    public const double TickGapMs = 60;

    /// <summary>The longest a flight can take, plus slack: the director's watchdog.</summary>
    public const double WatchdogMs = 4500;

    /// <summary>The base size of a star, in pixels, before its own scale.</summary>
    public const double StarPx = 26;

    public static int StarCount(int amount, MotionLevel level)
    {
        if (amount == 0 || level == MotionLevel.Off) return 0;
        var a = Math.Abs((double)amount);
        var n = amount > 0 ? Math.Clamp(8 + (int)Math.Round(a * 0.9), 10, 30)
                           : Math.Clamp((int)Math.Round(a / 35), 10, 22);
        return level == MotionLevel.Reduced ? (n + 1) / 2 : n;
    }

    /// <summary>Stars from the spot (<paramref name="sx"/>, <paramref name="sy"/>) to the wallet.</summary>
    public static SparkleStarPath[] Gain(double sx, double sy, double ex, double ey, int count, Random rng)
    {
        var stars = new SparkleStarPath[Math.Max(0, count)];
        for (var i = 0; i < stars.Length; i++)
        {
            var size = i % 4 == 0 ? 0.78 + rng.NextDouble() * 0.2 : 0.34 + rng.NextDouble() * 0.3;
            var a = -Math.PI / 2 + (rng.NextDouble() - 0.5) * 3.6;
            var burst = 34 + rng.NextDouble() * 70;
            var bx = sx + Math.Cos(a) * burst;
            var by = sy + Math.Sin(a) * burst * 0.8;
            var sway = (rng.NextDouble() - 0.5) * 150;
            var mx = bx + (ex - bx) * 0.55 + sway * 0.35;
            var my = by + (ey - by) * 0.55 - Math.Abs(sway) * 0.3;
            var spin = (rng.NextDouble() - 0.5) * 540;
            stars[i] = new SparkleStarPath(sx, sy, bx, by, mx, my, ex, ey, size, spin,
                i * 44 + rng.NextDouble() * 40, 1150 + rng.NextDouble() * 420, Leaving: false);
        }
        return stars;
    }

    /// <summary>Stars out of the wallet toward (<paramref name="ex"/>, <paramref name="ey"/>), spread.</summary>
    public static SparkleStarPath[] Spend(double sx, double sy, double ex, double ey, int count, Random rng)
    {
        var stars = new SparkleStarPath[Math.Max(0, count)];
        for (var i = 0; i < stars.Length; i++)
        {
            var size = i % 3 == 0 ? 0.8 : 0.4 + rng.NextDouble() * 0.3;
            var dx = (rng.NextDouble() - 0.5) * 220;
            var dy = (rng.NextDouble() - 0.5) * 60;
            var mx = sx + (ex + dx - sx) * 0.45 + (rng.NextDouble() - 0.5) * 120;
            var my = sy + (ey + dy - sy) * 0.45 - 30 - rng.NextDouble() * 40;
            stars[i] = new SparkleStarPath(sx, sy, mx, my, mx, my, ex + dx, ey + dy, size,
                (rng.NextDouble() - 0.5) * 420, i * 42, 900 + rng.NextDouble() * 320, Leaving: true);
        }
        return stars;
    }

    /// <summary>The number the wallet shows after <paramref name="landed"/> of <paramref name="count"/>
    /// stars; the last one always stands on the truth.</summary>
    public static int CountAt(int from, int to, int landed, int count)
        => count <= 0 || landed >= count ? to : from + (int)Math.Round((to - from) * (double)Math.Max(0, landed) / count);

    /// <summary>The bell for a landing at fill <paramref name="k"/> (0..1): they climb as the wallet fills.</summary>
    public static int BellFor(double k)
        => Math.Clamp((int)Math.Floor((double.IsNaN(k) ? 0 : k) * Bells), 0, Bells - 1);

    /// <summary>Where a star is <paramref name="ms"/> after the flight started; null before it leaves.</summary>
    public static SparkleStarPose? Pose(SparkleStarPath s, double ms)
    {
        var raw = (ms - s.DelayMs) / s.DurMs;
        if (raw < 0) return null;
        var p = Math.Clamp(raw, 0, 1);
        if (s.Leaving)
        {
            var q = Ease(p, 0.4, 0, 0.25, 1);
            return q < 0.45
                ? Lerp(s.X0, s.Y0, s.Size * 0.5, 0, 0, s.Bx, s.By, s.Size * 1.1, s.Spin * 0.5, 1, q / 0.45)
                : Lerp(s.Bx, s.By, s.Size * 1.1, s.Spin * 0.5, 1, s.X1, s.Y1, s.Size * 0.7, s.Spin, 0, (q - 0.45) / 0.55);
        }
        var e = Ease(p, 0.42, 0, 0.3, 1);
        if (e < 0.2) return Lerp(s.X0, s.Y0, s.Size * 0.3, 0, 0, s.Bx, s.By, s.Size * 1.15, s.Spin * 0.2, 1, e / 0.2);
        if (e < 0.62) return Lerp(s.Bx, s.By, s.Size * 1.15, s.Spin * 0.2, 1, s.Mx, s.My, s.Size, s.Spin * 0.6, 1, (e - 0.2) / 0.42);
        return Lerp(s.Mx, s.My, s.Size, s.Spin * 0.6, 1, s.X1, s.Y1, s.Size * 0.55, s.Spin, 1, (e - 0.62) / 0.38);
    }

    private static SparkleStarPose Lerp(double x0, double y0, double s0, double r0, double a0,
                                        double x1, double y1, double s1, double r1, double a1, double t)
        => new(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, s0 + (s1 - s0) * t, r0 + (r1 - r0) * t, a0 + (a1 - a0) * t);

    /// <summary>CSS cubic-bezier(x1, y1, x2, y2) at time <paramref name="t"/> (bisection on x).</summary>
    public static double Ease(double t, double x1, double y1, double x2, double y2)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double lo = 0, hi = 1, u = t;
        for (var i = 0; i < 24; i++)
        {
            u = (lo + hi) / 2;
            if (Bez(u, x1, x2) < t) lo = u; else hi = u;
        }
        return Bez(u, y1, y2);
    }

    private static double Bez(double u, double p1, double p2)
        => 3 * (1 - u) * (1 - u) * u * p1 + 3 * (1 - u) * u * u * p2 + u * u * u;
}
