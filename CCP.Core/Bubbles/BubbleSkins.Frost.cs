namespace ConditioningControlPanel.BubbleCore;

/// <summary>
/// Frost: the bubble freezes while it floats. Under an ice-tinted soap film a pale tint deepens, fine
/// speckles of rime gather round the wall and a six-armed snowflake grows from the middle with the bubble's
/// age, branching as it goes, turning slowly. The pop cracks the glass, then breaks into a soft puff, the
/// six arms as sticks, fine dots and many small flakes drifting down. Ported from the bubble lab.
/// </summary>
public static partial class BubbleSkins
{
    public const int MaxFrostSpecks = 60, FrostArms = 6, MaxFrostSegments = FrostArms * 5, FrostCracks = 5;
    public const int FrostSticks = 6, MaxFrostDots = 40, MaxFrostFlakes = 64;
    /// <summary>Seconds a bubble takes to freeze through; under Off motion it shows frozen through.</summary>
    public const double FrostFullAge = 4.5;
    /// <summary>The snowflake's arm length at full growth, as a share of the glass.</summary>
    public const double FrostArm = .62;
    private static readonly double[] FrostBranchAt = { .38, .68 };
    public const uint FrostWhiteArgb = 0xF2ECF8FF, FrostDotArgb = 0xE6F0FAFF, FrostPuffArgb = 0xCCC8E8FF;

    public static int FrostSpeckCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .8), 24, MaxFrostSpecks);
    public static int FrostDotCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .5), 16, MaxFrostDots);
    public static int FrostFlakeCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .8), 24, MaxFrostFlakes);

    /// <summary>How frozen the bubble is, 0.12 to 1: its age over <see cref="FrostFullAge"/> (Reduced half as fast,
    /// Off one frame: frozen through).</summary>
    public static double FrostGrowth(double seconds, double motion)
        => motion <= 0 ? 1 : Math.Clamp(seconds * Math.Clamp(motion, 0, 1) / FrostFullAge, .12, 1);

    /// <summary>The snowflake's slow turn, radians.</summary>
    public static double FrostSpin(int seed, double seconds, double motion) => seconds * Math.Clamp(motion, 0, 1) * .12 + Hash(seed, 3) * Tau;

    /// <summary>The rime: square specks (R = half their side) that fade in round the wall as the bubble freezes.</summary>
    private static int FrostInterior(int seed, double seconds, double motion, double radiusPx, Span<SkinPrim> dst)
    {
        int count = FrostSpeckCount(radiusPx);
        if (dst.Length < count || radiusPx <= 0) return 0;
        double g = FrostGrowth(seconds, motion), minR = MinPx / radiusPx;
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            double al = Math.Clamp((g - Hash(seed, i)) * 10, 0, 1);
            if (al <= 0) continue;
            double an = Hash(seed + 2, i) * Tau, rr = .64 + Hash(seed + 4, i) * .25, s = Math.Max(minR, .008 * (1 + Hash(seed + 6, i)));
            dst[n++] = new SkinPrim((float)(Math.Cos(an) * rr), (float)(Math.Sin(an) * rr), (float)s, (float)(.8 * al), SkinPrimKind.Speck);
        }
        return n;
    }

    /// <summary>The snowflake as line segments (x0, y0, x1, y1 in glass units, 4 floats each): six arms that lengthen
    /// with the growth and sprout two pairs of branches. Returns the segment count.</summary>
    public static int FrostFlake(int seed, double seconds, double motion, Span<float> seg)
    {
        if (seg.Length < MaxFrostSegments * 4) return 0;
        double g = FrostGrowth(seconds, motion), spin = FrostSpin(seed, seconds, motion), len = FrostArm * g;
        int n = 0;
        void Add(Span<float> s, double th, double x0, double y0, double x1, double y1)
        {
            double c = Math.Cos(th), sn = Math.Sin(th);
            s[n * 4] = (float)(x0 * c - y0 * sn); s[n * 4 + 1] = (float)(x0 * sn + y0 * c);
            s[n * 4 + 2] = (float)(x1 * c - y1 * sn); s[n * 4 + 3] = (float)(x1 * sn + y1 * c);
            n++;
        }
        for (int a = 0; a < FrostArms; a++)
        {
            double th = spin + (a + 1) * Tau / FrostArms;
            Add(seg, th, 0, 0, 0, -len);
            foreach (double f in FrostBranchAt)
            {
                if (g <= f) continue;
                double bl = .2 * (1 - f * .45) * Math.Min(1, (g - f) * 3), y = -FrostArm * f;
                Add(seg, th, 0, y, bl * .8, y - bl * .6);
                Add(seg, th, 0, y, -bl * .8, y - bl * .6);
            }
        }
        return n;
    }

    /// <summary>The direction of crack <paramref name="i"/> of the pop, radians.</summary>
    public static double FrostCrack(int seed, int i) => (double)i / FrostCracks * Tau + Hash(seed, 9) * Tau;

    private static int FrostBurst(int seed, double radiusPx, double motion, Span<SkinParticle> dst)
    {
        if (dst.Length < 1 + FrostSticks + MaxFrostDots + MaxFrostFlakes || radiusPx <= 0 || motion <= 0) return 0;
        var rnd = new Mulberry((uint)seed);
        float m = (float)motion, minS = (float)(MinPx / radiusPx);
        double ph = Hash(seed, 3) * Tau;
        int n = 0;
        dst[n++] = new SkinParticle(0, 0, 0, 0, .7f, .7f, SkinParticleKind.Puff, Grow: 1.3f, Argb: FrostPuffArgb);

        // The six arms break off as sticks, tumbling as they fall.
        for (int a = 0; a < FrostSticks; a++)
        {
            double an = a / (double)FrostSticks * Tau + ph, v = 1.2 + rnd.Next();
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * .3), (float)(Math.Sin(an) * .3), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v) * m, (float)(.9 + rnd.Next() * .4), .5f, SkinParticleKind.Stick, Rise: 6f * m * m, Drag: 1.2f,
                Rot: (float)an, Spin: (float)((rnd.Next() - .5) * 9) * m, Argb: FrostWhiteArgb);
        }

        // Fine dots and many small flakes that drift down and sway.
        int dots = FrostDotCount(radiusPx);
        for (int i = 0; i < dots; i++)
        {
            double an = rnd.Next() * Tau, v = 1 + rnd.Next() * 4;
            float life = (float)(.7 + rnd.Next() * 1.1), size = Math.Max(minS, (float)(.008 + rnd.Next() * .012));
            float swf = (float)(2 + rnd.Next() * 3), sw0 = (float)(rnd.Next() * 6);
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * .5), (float)(Math.Sin(an) * .5), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v) * m, life, size, SkinParticleKind.Dot, Rise: 1.2f * m * m, Drag: 3.4f,
                SwayAmp: .6f * m, SwayFreq: swf, SwayPhase: sw0, Argb: FrostDotArgb);
        }
        int flakes = FrostFlakeCount(radiusPx);
        for (int i = 0; i < flakes; i++)
        {
            double an = rnd.Next() * Tau, v = .8 + rnd.Next() * 3;
            float life = (float)(1.1 + rnd.Next()), size = Math.Max(minS, (float)(.014 + rnd.Next() * .024));
            float rot = (float)(rnd.Next() * 3), spin = (float)((rnd.Next() - .5) * 3) * m;
            float swf = (float)(2 + rnd.Next() * 3), sw0 = (float)(rnd.Next() * 6);
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * .5), (float)(Math.Sin(an) * .5), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v) * m, life, size, SkinParticleKind.Flake, Rise: 1.6f * m * m, Drag: 3f,
                SwayAmp: .8f * m, SwayFreq: swf, SwayPhase: sw0, Rot: rot, Spin: spin, Argb: FrostWhiteArgb);
        }
        return n;
    }
}
