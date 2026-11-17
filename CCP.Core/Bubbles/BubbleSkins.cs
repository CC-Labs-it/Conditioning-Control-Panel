namespace ConditioningControlPanel.BubbleCore;

/// <summary>Bubble skin ids. <see cref="Soap"/> is today's look and the default; the rest are reserved
/// names for skins still to be drawn. Only a skin in <see cref="IsDrawn"/> changes anything.</summary>
public static class BubbleSkinIds
{
    public const string Soap = "soap";
    public const string Champagne = "champagne";
    public const string Heart = "heart";
    public const string Spiral = "spiral";
    public const string Marble = "marble";
    public const string Disco = "disco";
    public const string Frost = "frost";

    public static readonly IReadOnlyList<string> All = new[] { Soap, Champagne, Heart, Spiral, Marble, Disco, Frost };

    /// <summary>True for a skin that has a draw today (soap draws nothing extra).</summary>
    public static bool IsDrawn(string? id) => id is Champagne or Heart or Marble or Spiral;
}

/// <summary>The one place a new bubble learns its skin. Soap, unless a DEBUG build names a drawn
/// skin in CCP_BUBBLE_SKIN. No shop, ownership or setting feeds it yet.</summary>
public static class BubbleSkinChoice
{
    public const string EnvVar = "CCP_BUBBLE_SKIN";

    public static string Current()
    {
#if DEBUG
        return Resolve(Environment.GetEnvironmentVariable(EnvVar));
#else
        return BubbleSkinIds.Soap;
#endif
    }

    /// <summary>A drawn skin id (any case, trimmed), else soap.</summary>
    public static string Resolve(string? requested)
    {
        var id = requested?.Trim().ToLowerInvariant();
        return BubbleSkinIds.IsDrawn(id) ? id! : BubbleSkinIds.Soap;
    }
}

/// <summary>Heart: <see cref="HeartBody"/> is the heart itself (R = its scale, Alpha = the beat 0..1);
/// <see cref="HeartRipple"/> is the small heart a beat sends out (R = its scale).</summary>
public enum SkinPrimKind : byte { Bead, Ring, Foam, HeartBody, HeartRipple }

/// <summary>One interior shape in the bubble's unit circle (y down, radius 1 = the glass).</summary>
public readonly record struct SkinPrim(float X, float Y, float R, float Alpha, SkinPrimKind Kind);

/// <summary><see cref="Shard"/>: a Marble shard, corners from <see cref="BubbleSkins.MarbleShard"/> by its Index.
/// <see cref="Star"/>: a four-point glint whose size twinkles at Grow radians per second.</summary>
public enum SkinParticleKind : byte { Dot, Ring, Spark, Wave, Mist, Heart, HeartWave, Shard, Star, Orbit }

/// <summary>One pop particle at birth, in glass radii (position) and radii per second (velocity).
/// <see cref="Rise"/> is a constant pull (negative = up), <see cref="Drag"/> slows it per second,
/// <see cref="Grow"/> widens a wave or a mist over its life. <see cref="Rot"/> and <see cref="Spin"/>
/// turn a shaped particle (radians, radians per second); <see cref="Argb"/> is its colour (0 = the
/// painter's default for the kind).</summary>
public readonly record struct SkinParticle(float X, float Y, float Vx, float Vy, float Life, float Size,
    SkinParticleKind Kind, float Rise = 0, float Drag = 0, float SwayAmp = 0, float SwayFreq = 0, float SwayPhase = 0, float Grow = 0,
    float Rot = 0, float Spin = 0, uint Argb = 0, ushort Index = 0);

/// <summary>Where a particle is <c>age</c> seconds after the pop. Alive = false once its life is spent.</summary>
public readonly record struct SkinParticleState(float X, float Y, float Size, float Alpha, bool Alive, float Rot = 0);

/// <summary>
/// Skin rules: pure and deterministic. Given a skin, a per-bubble seed, a time and a motion scale
/// (1 Full, 0.5 Reduced, 0 Off = one fixed frame), they return the interior shapes; given a skin and
/// a seed they return the pop burst. Counts scale with the bubble's glass radius in pixels and are
/// capped, so a field of 40 bubbles at 30 fps stays cheap. Interior shapes never leave the glass.
/// </summary>
public static partial class BubbleSkins
{
    private const double Tau = Math.PI * 2;
    /// <summary>Interior shapes sit inside this share of the glass (the lab clips at 0.93).</summary>
    public const double Inner = 0.93;
    /// <summary>A shape is never drawn smaller than this many pixels, however small the bubble.</summary>
    public const double MinPx = 0.7;

    public const int MaxBeads = 44, MaxRings = 3, MaxFoam = 22, Nozzles = 3;
    public const int MaxInterior = MaxBeads + MaxRings + MaxFoam + Nozzles;
    public const int MaxFizz = 48, MaxSparks = 12, Mists = 3;
    public const int MaxBurst = 1 + MaxFizz + MaxSparks + Mists;
    /// <summary>Room for the largest burst of any skin: the layer sizes every burst buffer by it.</summary>
    public const int BurstCapacity = 96;

    /// <summary>True for a skin that draws its own body in place of the soap sprite (the hit area stays the circle).</summary>
    public static bool OwnsBody(string? skin) => skin is BubbleSkinIds.Heart or BubbleSkinIds.Marble;

    /// <summary>True for a skin drawn under the soap sprite (inside the film), not over it.</summary>
    public static bool UnderShell(string? skin) => skin is BubbleSkinIds.Spiral;

    /// <summary>Seconds the skin's burst spends on the dying body (squeeze, cracks, wind-in) before its
    /// particles fly. The pop itself pays and sounds at once; this is drawing only. 0 = no pre-burst.</summary>
    public static double PreBurst(string? skin, bool gentle) => skin switch
    {
        BubbleSkinIds.Heart => .16,
        BubbleSkinIds.Marble => gentle ? .32 : .2,   // Gentle: the flash becomes a glow that rises over 0.3 s
        BubbleSkinIds.Spiral => .46,
        _ => 0,
    };

    public static int BeadCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 0.7), 16, MaxBeads);
    public static int FoamCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 0.32), 8, MaxFoam);
    public static int RingCount(double radiusPx) => radiusPx < 32 ? 2 : MaxRings;
    public static int FizzCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 0.8), 18, MaxFizz);
    public static int SparkCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 0.2), 6, MaxSparks);

    /// <summary>The interior at <paramref name="seconds"/> after spawn. Returns how many shapes it wrote.</summary>
    public static int Interior(string? skin, int seed, double seconds, double motion, double radiusPx, Span<SkinPrim> dst)
    {
        if (skin == BubbleSkinIds.Heart) return HeartInterior(seed, seconds, motion, radiusPx, dst);
        if (skin != BubbleSkinIds.Champagne || dst.Length < MaxInterior || radiusPx <= 0) return 0;
        double t = seconds * Math.Clamp(motion, 0, 1);
        double minR = MinPx / radiusPx;
        int n = 0;

        // Fizz: each bead takes its own crooked way up from one of three seed points; the stream breathes.
        double breathe = 1 + .28 * Math.Sin(t * .9 + seed);
        int beads = BeadCount(radiusPx);
        for (int i = 0; i < beads; i++)
        {
            double h1 = Hash(seed, i), h2 = Hash(seed + 7, i), h3 = Hash(seed + 11, i), h4 = Hash(seed + 17, i), h5 = Hash(seed + 23, i);
            double u = Frac(h1 + t * (.65 + h2 * 1.15));
            double y = Lerp(.82, -.6, Math.Pow(u, .85));
            double wd = (.02 + .2 * Math.Pow(u, .8)) * breathe;
            double x = (i % 3 - 1) * .08 * (1 - u)
                + wd * (.62 * Math.Sin(u * (5 + h3 * 9) + h4 * Tau + t * (.6 + h5))
                      + .38 * Math.Sin(u * (13 + h5 * 14) + h3 * Tau - t * (1.3 + h4)))
                + (h4 - .5) * .34 * u * u;
            double s = Math.Max(minR, .011 + .026 * h3 * h3 + .007 * u);
            double al = (.45 + .55 * h5) * Math.Min(1, u * 8) * Math.Min(1, (1 - u) * 10);
            dst[n++] = Inside(x, y, s, al, SkinPrimKind.Bead);
        }

        // A few slow fat ones wandering up the sides.
        int rings = RingCount(radiusPx);
        for (int i = 0; i < rings; i++)
        {
            double h1 = Hash(seed + 31, i), h2 = Hash(seed + 37, i), h3 = Hash(seed + 41, i);
            double u = Frac(h1 + t * (.15 + h2 * .12));
            double y = Lerp(.7, -.62, u);
            double x = (h3 - .5) * 1.1 * Math.Sqrt(Math.Max(0, 1 - Math.Pow(y / .92, 2))) + Math.Sin(t * 1.4 + i * 2) * .05;
            double s = Math.Max(minR, .03 + .03 * h2);
            double al = .6 * Math.Min(1, u * 6) * Math.Min(1, (1 - u) * 6);
            dst[n++] = Inside(x, y, s, al, SkinPrimKind.Ring);
        }

        // Foam gathering under the top of the glass.
        int foam = FoamCount(radiusPx);
        for (int i = 0; i < foam; i++)
        {
            double h = Hash(seed + 3, i), an = -Math.PI / 2 + (h - .5) * 1.9, life = Frac(h * 9 + t * (.5 + h));
            double rr = .84 - Hash(seed + 5, i) * .2;
            double s = Math.Max(minR, (.012 + .022 * Hash(seed + 9, i)) * (.4 + .6 * Math.Sin(life * Math.PI)));
            double x = Math.Cos(an) * rr + Math.Sin(t * 3 + i) * .008;
            double y = Math.Sin(an) * rr + Math.Cos(t * 2.3 + i * 1.7) * .01;
            dst[n++] = Inside(x, y, s, .5, SkinPrimKind.Foam);
        }

        // The three seed points at the bottom.
        for (int i = -1; i <= 1; i++)
            dst[n++] = Inside(i * .08, .82 - Math.Abs(i) * .01, Math.Max(minR, .016), .85, SkinPrimKind.Bead);
        return n;
    }

    /// <summary>The pop burst of one bubble. Motion 0 (Off) bursts nothing; Reduced flies half as far.</summary>
    public static int Burst(string? skin, int seed, double radiusPx, double motion, Span<SkinParticle> dst)
    {
        motion = Math.Clamp(motion, 0, 1);
        if (skin == BubbleSkinIds.Heart) return HeartBurst(seed, radiusPx, motion, dst);
        if (skin == BubbleSkinIds.Marble) return MarbleBurst(seed, radiusPx, motion, dst);
        if (skin == BubbleSkinIds.Spiral) return SpiralBurst(seed, radiusPx, motion, dst);
        if (skin != BubbleSkinIds.Champagne || dst.Length < MaxBurst || radiusPx <= 0 || motion <= 0) return 0;
        var rnd = new Mulberry((uint)seed);
        float m = (float)motion, minS = (float)(MinPx / radiusPx);
        int n = 0;
        dst[n++] = new SkinParticle(0, 0, 0, 0, .4f, 1f, SkinParticleKind.Wave, Grow: .9f);

        // A cloud of fine foam: dots and small rings that rise and sway.
        int fizz = FizzCount(radiusPx);
        for (int i = 0; i < fizz; i++)
        {
            double a = -Math.PI / 2 + (rnd.Next() - .5) * Tau * .85, v = 1 + rnd.Next() * 5.5, d = rnd.Next() * .8;
            float life = (float)(.5 + rnd.Next() * 1.1), size = Math.Max(minS, (float)(.012 + rnd.Next() * .028));
            float swf = (float)(6 + rnd.Next() * 7), sw0 = (float)(rnd.Next() * 6);
            dst[n++] = new SkinParticle((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d),
                (float)(Math.Cos(a) * v) * m, (float)(Math.Sin(a) * v) * m, life, size,
                i % 3 != 0 ? SkinParticleKind.Dot : SkinParticleKind.Ring,
                Rise: -3.4f * m, Drag: 2.6f, SwayAmp: .9f * m, SwayFreq: swf, SwayPhase: sw0);
        }

        // A quick flat spray from the middle.
        int sparks = SparkCount(radiusPx);
        for (int i = 0; i < sparks; i++)
        {
            double a = rnd.Next() * Tau, v = 3 + rnd.Next() * 6;
            float life = (float)(.2 + rnd.Next() * .25);
            dst[n++] = new SkinParticle(0, 0, (float)(Math.Cos(a) * v) * m, (float)(Math.Sin(a) * v) * m, life,
                Math.Max(minS, .012f), SkinParticleKind.Spark, Drag: 5f);
        }

        // The mist the pop leaves, drifting up and spreading.
        for (int i = 0; i < Mists; i++)
        {
            double a = rnd.Next() * Tau, d = rnd.Next() * .35;
            dst[n++] = new SkinParticle((float)(Math.Cos(a) * d), (float)(Math.Sin(a) * d), 0, -.45f * m,
                (float)(.7 + rnd.Next() * .3), (float)(.55 + rnd.Next() * .25), SkinParticleKind.Mist, Drag: 1f, Grow: .8f);
        }
        return n;
    }

    /// <summary>Closed form of the lab's per-step physics (drag, constant pull, sway), so a particle's
    /// place depends only on its age: no state to step, nothing to drift between frame rates.</summary>
    public static SkinParticleState At(in SkinParticle p, double age)
    {
        if (age < 0 || age >= p.Life) return new SkinParticleState(p.X, p.Y, 0, 0, false);
        double k = p.Drag, x, y;
        if (k > 1e-6)
        {
            double e = (1 - Math.Exp(-k * age)) / k;
            double term = p.Rise / k;
            x = p.X + p.Vx * e;
            y = p.Y + term * age + (p.Vy - term) * e;
        }
        else
        {
            x = p.X + p.Vx * age;
            y = p.Y + p.Vy * age + .5 * p.Rise * age * age;
        }
        if (p.Kind == SkinParticleKind.Orbit) (x, y) = OrbitAt(p, age);
        if (p.SwayAmp != 0 && p.SwayFreq > 0)
            x += p.SwayAmp / p.SwayFreq * (Math.Cos(p.SwayPhase) - Math.Cos(p.SwayFreq * age + p.SwayPhase));
        double u = age / p.Life;
        double alpha = Math.Min(1, (1 - u) / .45);
        double size = p.Kind switch
        {
            SkinParticleKind.Wave => p.Size * (1 + p.Grow * (1 - (1 - u) * (1 - u))),
            SkinParticleKind.Mist or SkinParticleKind.HeartWave => p.Size * (1 + p.Grow * u),
            SkinParticleKind.Star => p.Size * (.35 + .65 * Math.Abs(Math.Sin(age * p.Grow))),
            SkinParticleKind.Orbit => p.Size * (1 - u * .6),
            _ => p.Size,
        };
        return new SkinParticleState((float)x, (float)y, (float)size, (float)alpha, true, (float)(p.Rot + p.Spin * age));
    }

    /// <summary>Longest life in a burst: the layer keeps it until then.</summary>
    public static float LifeOf(ReadOnlySpan<SkinParticle> parts)
    {
        float l = 0;
        foreach (var p in parts) l = Math.Max(l, p.Life);
        return l;
    }

    private static SkinPrim Inside(double x, double y, double r, double alpha, SkinPrimKind kind)
    {
        // Ring strokes reach a fifth past their radius; keep the whole shape inside the glass.
        double reach = kind == SkinPrimKind.Ring ? r * 1.1 : r;
        double max = Inner - reach, d = Math.Sqrt(x * x + y * y);
        if (d > max && d > 1e-9) { double f = Math.Max(0, max) / d; x *= f; y *= f; }
        return new SkinPrim((float)x, (float)y, (float)r, (float)Math.Clamp(alpha, 0, 1), kind);
    }

    private static double Frac(double v) => v - Math.Floor(v);
    private static double Lerp(double a, double b, double k) => a + (b - a) * k;

    /// <summary>The lab's integer hash, bit for bit (Math.imul is the low 32 bits of the product).</summary>
    internal static double Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 374761393u + (uint)b * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967296.0;
        }
    }

    /// <summary>The lab's mulberry32 stream, so a seed always gives the same burst.</summary>
    private struct Mulberry
    {
        private uint _s;
        public Mulberry(uint seed) { _s = seed; }
        public double Next()
        {
            unchecked
            {
                _s += 0x6D2B79F5u;
                uint t = _s;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }
}
