namespace ConditioningControlPanel.BubbleCore;

/// <summary>
/// Heart: the bubble is a plump heart with a soft halo edge that beats lub-dub, glows from the
/// middle on each beat and sends a small heart rippling out to the wall. The pop squeezes the
/// heart, then throws small hearts and fine dust that rise and sway. Ported from the bubble lab.
/// </summary>
public static partial class BubbleSkins
{
    /// <summary>How much a beat swells the heart.</summary>
    public const double HeartPulse = .075;
    public const int MaxHearts = 24, MaxHeartDust = 36;

    public static int HeartCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .45), 10, MaxHearts);
    public static int HeartDustCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .6), 14, MaxHeartDust);

    private static readonly uint[] HeartTones = { 0xFFFF2D55, 0xFFFF5C7A, 0xFFFF8FA3, 0xFFE0113F, 0xFFFFC2CF };
    public const uint HeartWaveArgb = 0xE6FFAABE, HeartDustArgb = 0xE6FFCDD7;

    /// <summary>The beat at <paramref name="t"/> seconds: its phase 0..1 and a lub-dub value (a big bump, then a smaller one).</summary>
    public static (double Phase, double Value) HeartBeat(int seed, double t)
    {
        double p = Frac(t * 1.15 + seed * .137);
        static double Bump(double p, double c, double w) => Math.Exp(-Math.Pow((p - c) / w, 2));
        return (p, Bump(p, .06, .045) + .6 * Bump(p, .26, .05));
    }

    /// <summary>The pre-burst squeeze at k (0..1): in to 0.85, then out past the rim as it goes.</summary>
    public static double HeartSqueeze(double k) => k <= 0 ? 1 : k < .5 ? 1 - k * .3 : .85 + (k - .5) * .8;

    private static int HeartInterior(int seed, double seconds, double motion, double radiusPx, Span<SkinPrim> dst)
    {
        if (dst.Length < 2 || radiusPx <= 0) return 0;
        var (p, v) = HeartBeat(seed, seconds * Math.Clamp(motion, 0, 1));
        double e = Frac(p - .06);
        dst[0] = new SkinPrim(0, 0, (float)(1 + HeartPulse * v), (float)Math.Clamp(v, 0, 1), SkinPrimKind.HeartBody);
        dst[1] = new SkinPrim(0, 0, (float)(.16 + .8 * e), (float)((1 - e) * .7), SkinPrimKind.HeartRipple);
        return 2;
    }

    private static int HeartBurst(int seed, double radiusPx, double motion, Span<SkinParticle> dst)
    {
        if (dst.Length < 1 + MaxHearts + MaxHeartDust || radiusPx <= 0 || motion <= 0) return 0;
        var rnd = new Mulberry((uint)seed);
        float m = (float)motion, minS = (float)(MinPx / radiusPx);
        int n = 0;
        dst[n++] = new SkinParticle(0, 0, 0, 0, .45f, 1f, SkinParticleKind.HeartWave, Grow: 1.1f, Argb: HeartWaveArgb);

        // Small hearts from near the middle: they rise, sway and turn a little.
        int hearts = HeartCount(radiusPx);
        for (int i = 0; i < hearts; i++)
        {
            double a = rnd.Next() * Tau, v = 1.2 + rnd.Next() * 4;
            float life = (float)(.8 + rnd.Next() * .9), size = Math.Max(minS, (float)(.04 + rnd.Next() * rnd.Next() * .09));
            float rot = (float)((rnd.Next() - .5) * .8), spin = (float)((rnd.Next() - .5) * 1.5);
            float swf = (float)(3 + rnd.Next() * 3), sw0 = (float)(rnd.Next() * 6);
            dst[n++] = new SkinParticle((float)(Math.Cos(a) * .3), (float)(Math.Sin(a) * .3),
                (float)(Math.Cos(a) * v) * m, (float)(Math.Sin(a) * v) * m, life, size, SkinParticleKind.Heart,
                Rise: -1.3f * m, Drag: 2.1f, SwayAmp: .7f * m, SwayFreq: swf, SwayPhase: sw0,
                Rot: rot, Spin: spin * m, Argb: HeartTones[i % HeartTones.Length]);
        }

        // Fine pink dust, quick and short.
        int dust = HeartDustCount(radiusPx);
        for (int i = 0; i < dust; i++)
        {
            double a = rnd.Next() * Tau, v = 2 + rnd.Next() * 5;
            float life = (float)(.3 + rnd.Next() * .4), size = Math.Max(minS, (float)(.01 + rnd.Next() * .012));
            dst[n++] = new SkinParticle(0, 0, (float)(Math.Cos(a) * v) * m, (float)(Math.Sin(a) * v) * m, life, size,
                SkinParticleKind.Dot, Rise: -.6f * m, Drag: 3.6f, Argb: HeartDustArgb);
        }
        return n;
    }

    private static float[]? s_heart;

    /// <summary>The lab's plump heart outline as x,y pairs (unit size, y down): the classic curve pulled a fifth
    /// of the way toward a circle, then smoothed four times so the tip and the cleft go soft. Built once.</summary>
    public static ReadOnlySpan<float> HeartOutline() => s_heart ??= BuildHeart(120);

    private static float[] BuildHeart(int n)
    {
        var x = new double[n]; var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = (double)i / n * Tau;
            double hx = 16 * Math.Pow(Math.Sin(a), 3) / 17;
            double hy = -(13 * Math.Cos(a) - 5 * Math.Cos(2 * a) - 2 * Math.Cos(3 * a) - Math.Cos(4 * a)) / 17 - .13;
            x[i] = Lerp(hx, Math.Sin(a) * .88, .2);
            y[i] = Lerp(hy, -Math.Cos(a) * .86 + .02, .2);
        }
        int[] wt = { 1, 2, 3, 2, 1 };
        for (int pass = 0; pass < 4; pass++)
        {
            var nx = new double[n]; var ny = new double[n];
            for (int i = 0; i < n; i++)
                for (int d = -2; d <= 2; d++)
                {
                    int q = (i + d + n) % n;
                    nx[i] += x[q] * wt[d + 2] / 9.0; ny[i] += y[q] * wt[d + 2] / 9.0;
                }
            x = nx; y = ny;
        }
        var pts = new float[n * 2];
        for (int i = 0; i < n; i++) { pts[i * 2] = (float)x[i]; pts[i * 2 + 1] = (float)y[i]; }
        return pts;
    }
}
