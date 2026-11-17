using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.BubbleCore;

/// <summary>
/// Spiral: a Loom spiral inside the soap glass, a different one for each bubble (the Loom's own dice,
/// <see cref="LoomSpec.Random"/>, seeded by the bubble), turning slowly. Only the turning weaves (styles 0
/// to 3) are used: their turn is a pure angular offset, so the spiral is baked once per bubble into a small
/// picture and the picture is rotated. The pop winds in (faster turn, shrinking, two trailing echoes), then
/// flings particles out along the arms in the spiral's own colours. Ported from the bubble lab.
/// </summary>
public static partial class BubbleSkins
{
    public const int MinSpiralArt = 40, MaxSpiralArt = 128;
    public const int MaxSpiralOrbits = 72, MaxSpiralDust = 22;
    /// <summary>The spiral picture's radius as a share of the glass.</summary>
    public const double SpiralReach = .9;

    public static int SpiralOrbitCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 1.2), 36, MaxSpiralOrbits);
    public static int SpiralDustCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .5), 10, MaxSpiralDust);

    /// <summary>The side of the baked picture for a glass radius in pixels: about one texel per pixel, capped.</summary>
    public static int SpiralArtSize(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 2 * SpiralReach), MinSpiralArt, MaxSpiralArt);

    /// <summary>This bubble's spiral: the Loom's dice with the bubble's seed, a turning weave only.</summary>
    public static LoomSpec SpiralSpec(int seed)
    {
        var rng = new Random(seed);
        var spec = LoomSpec.Random(rng);
        if (spec.Layer.Style > 3) spec.Layer.Style = rng.Next(4);
        spec.Layer2.Style = 0;
        return spec;
    }

    /// <summary>Bake one weave of <paramref name="spec"/> (the second when <paramref name="second"/>) into
    /// premultiplied RGBA (size x size x 4), fading toward the rim so it sits inside the film. False when the
    /// spec has no second weave.</summary>
    public static bool SpiralBake(LoomSpec spec, bool second, int size, byte[] rgba)
    {
        if (second && !spec.Layer2On) return false;
        var one = new LoomSpec
        {
            Layer = second ? spec.Layer2 : spec.Layer, Layer2On = false, Glow = second ? 0 : spec.Glow,
            WobAmp = spec.WobAmp, WobFreq = spec.WobFreq, WobCycles = spec.WobCycles,
        };
        var loom = new LoomSpiral(size);
        loom.Add(one, 0, 0, 1);
        loom.CopyTo(rgba);
        double c = size / 2.0;
        for (int y = 0, i = 0; y < size; y++)
            for (int x = 0; x < size; x++, i += 4)
            {
                double px = (x + .5 - c) / c, py = (y + .5 - c) / c, r = Math.Sqrt(px * px + py * py);
                double k = r - .74; k = Math.Clamp(k / .26, 0, 1);
                float edge = (float)(1 - k * k * (3 - 2 * k) * .85);
                for (int ch = 0; ch < 4; ch++) rgba[i + ch] = (byte)(rgba[i + ch] * edge + .5f);
            }
        return true;
    }

    /// <summary>How fast this bubble's spiral turns, radians per second.</summary>
    public static double SpiralSpin(int seed) => .5 + Hash(seed, 4) * .7;

    /// <summary>How far a weave has turned (radians, before its direction). <paramref name="k"/> is the pop's wind-in
    /// 0..1 (it adds up to 17 radians), <paramref name="lag"/> holds an echo back.</summary>
    public static double SpiralTurn(int seed, double seconds, double motion, double k, double lag, bool second)
        => seconds * Math.Clamp(motion, 0, 1) * SpiralSpin(seed) * (second ? .7 : 1) + k * k * 17 - lag;

    /// <summary>The spiral's scale: a slow breath, and the wind-in shrinking it to nearly nothing.</summary>
    public static double SpiralScale(int seed, double seconds, double motion, double k)
        => (1 - k * k * .96) * (1 + Math.Sin(seconds * Math.Clamp(motion, 0, 1) * 1.7 + seed) * .02);

    /// <summary>A trailing echo of the wind-in, starting <paramref name="delay"/> later.</summary>
    public static double SpiralEchoScale(double k, double delay) => 1 - Math.Pow(Math.Max(0, k - delay), 2) * .96;

    private static int SpiralBurst(int seed, double radiusPx, double motion, Span<SkinParticle> dst)
    {
        if (dst.Length < 1 + MaxSpiralOrbits + MaxSpiralDust || radiusPx <= 0 || motion <= 0) return 0;
        var spec = SpiralSpec(seed);
        int ncol = spec.Layer.Colors.Length + (spec.Layer2On ? spec.Layer2.Colors.Length : 0);
        uint Col(int i) => 0xFF000000u | (i < spec.Layer.Colors.Length ? spec.Layer.Colors[i] : spec.Layer2.Colors[i - spec.Layer.Colors.Length]);
        int arms = Math.Max(3, spec.Layer.Arms), dir = spec.Layer.Direction;
        var rnd = new Mulberry((uint)seed);
        float m = (float)motion, minS = (float)(MinPx / radiusPx);
        int n = 0;
        dst[n++] = new SkinParticle(0, 0, 0, 0, .42f, 1f, SkinParticleKind.Wave, Grow: 1.1f, Argb: 0xE6FFFFFF);

        // Particles fling out along each arm, still turning, slowing as they go (Orbit: X is the start radius,
        // Vx the outward speed, Vy the turn speed, Rot the start angle).
        int per = Math.Max(4, SpiralOrbitCount(radiusPx) / arms);
        for (int arm = 0; arm < arms; arm++)
            for (int j = 0; j < per; j++)
            {
                double f = (double)j / per;
                double ang = (double)arm / arms * Tau + f * 2.6 * dir + (rnd.Next() - .5) * .08;
                double vr = 1.5 + f * 3.4 + rnd.Next() * .4, life = .6 + f * .5 + rnd.Next() * .2;
                dst[n++] = new SkinParticle(.04f, 0, (float)vr * m, (float)(dir * (8.5 - f * 5.5)) * m, (float)life,
                    Math.Max(minS, (float)(.034 - f * .02)), SkinParticleKind.Orbit, Drag: 2.4f, Rot: (float)ang, Argb: Col(arm % ncol));
            }

        int dust = SpiralDustCount(radiusPx);
        for (int i = 0; i < dust; i++)
        {
            double a = rnd.Next() * Tau, v = 3 + rnd.Next() * 5;
            dst[n++] = new SkinParticle(0, 0, (float)(Math.Cos(a) * v) * m, (float)(Math.Sin(a) * v) * m, (float)(.2 + rnd.Next() * .25),
                Math.Max(minS, .011f), SkinParticleKind.Dot, Drag: 5f, Argb: 0xE6FFFFFF);
        }
        return n;
    }

    /// <summary>An Orbit particle's place: closed form of the lab's polar step (outward speed slows by Drag,
    /// the turn slows by 1.6 a second), so it depends only on its age.</summary>
    private static (double X, double Y) OrbitAt(in SkinParticle p, double age)
    {
        double k = Math.Max(1e-6, p.Drag);
        double rad = p.X + p.Vx * (1 - Math.Exp(-k * age)) / k;
        double ang = p.Rot + p.Vy * (1 - Math.Exp(-1.6 * age)) / 1.6;
        return (Math.Cos(ang) * rad, Math.Sin(ang) * rad);
    }
}
