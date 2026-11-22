namespace ConditioningControlPanel.BubbleCore;

/// <summary>One four-cornered patch of a Marble vane in glass units (y down), with its colour.</summary>
public readonly record struct SkinQuad(float X0, float Y0, float X1, float Y1, float X2, float Y2, float X3, float Y3, uint Argb);

/// <summary>
/// Marble: a solid glass body with three vanes wound round one axis inside. The twist tightens and
/// loosens, the axis tumbles, and the colours travel through a palette, so no two moments match. The
/// pop cracks the glass from the middle, flashes (a slow glow under Gentle visuals), then falls as
/// shards, glass dust and a few glints. Ported from the bubble lab.
/// </summary>
public static partial class BubbleSkins
{
    public const int MarbleVaneCount = 3, MaxVaneSteps = 22, MaxMarbleQuads = MarbleVaneCount * MaxVaneSteps;
    public const int MarbleCrackCount = 9, MarbleCrackStops = 5, MarbleShards = MarbleCrackCount * 4;
    public const int MaxMarbleDust = 40, MaxMarbleGlints = 8;

    public static int VaneSteps(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .3), 10, MaxVaneSteps);
    public static int MarbleDustCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .6), 18, MaxMarbleDust);
    public static int MarbleGlintCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .12), 4, MaxMarbleGlints);

    private static readonly double[,] MarbleCols =
    {
        { 255, 45, 142 }, { 138, 92, 255 }, { 34, 211, 238 }, { 57, 255, 157 }, { 255, 217, 77 }, { 255, 106, 47 }, { 255, 255, 255 },
    };
    public const uint MarbleGlassArgb = 0x6BCDE4FA, MarbleWhiteArgb = 0xF2FFFFFF;

    /// <summary>The marble palette at <paramref name="u"/> (wraps every 1), eased between neighbours.</summary>
    public static (double R, double G, double B) MarblePal(double u)
    {
        int n = MarbleCols.GetLength(0);
        double x = Frac(u) * n; int i = (int)Math.Floor(x) % n, j = (i + 1) % n;
        double f = x - Math.Floor(x); f = f * f * (3 - 2 * f);
        return (Lerp(MarbleCols[i, 0], MarbleCols[j, 0], f), Lerp(MarbleCols[i, 1], MarbleCols[j, 1], f), Lerp(MarbleCols[i, 2], MarbleCols[j, 2], f));
    }

    /// <summary>The breath of colour the glass itself picks up from what is inside.</summary>
    public static (double R, double G, double B) MarbleTint(int seed, double seconds, double motion)
        => MarblePal(seconds * Math.Clamp(motion, 0, 1) * .09 + seed * .013 + .5);

    /// <summary>The tumble of the vanes' axis, radians.</summary>
    public static double MarbleTumble(int seed, double seconds, double motion)
        => Math.Sin(seconds * Math.Clamp(motion, 0, 1) * .33 + seed) * .45;

    /// <summary>The shake of the cracking glass, in glass radii (a new offset every 1/60 s).</summary>
    public static (double X, double Y) MarbleShake(int seed, double seconds)
    {
        int f = (int)Math.Floor(seconds * 60);
        return ((Hash(seed, f) - .5) * .07, (Hash(seed + 1, f) - .5) * .07);
    }

    /// <summary>The vanes as quads, back to front (draw in order). Count scales with size; all inside 0.86 of the glass.</summary>
    public static int MarbleVanes(int seed, double seconds, double motion, double radiusPx, Span<SkinQuad> dst)
    {
        if (dst.Length < MaxMarbleQuads || radiusPx <= 0) return 0;
        double t = seconds * Math.Clamp(motion, 0, 1);
        int steps = VaneSteps(radiusPx), n = 0;
        double tw = 2.3 + Math.Sin(t * .47 + seed * .3) * .9, ph = t * 1.05 + seed;
        Span<float> depth = stackalloc float[MaxMarbleQuads];
        for (int j = 0; j < MarbleVaneCount; j++)
        {
            double y0 = -.84;
            Vane(y0, j, tw, ph, out double x0, out double w0, out double c0);
            for (int i = 1; i <= steps; i++)
            {
                double y1 = Lerp(-.84, .84, (double)i / steps);
                Vane(y1, j, tw, ph, out double x1, out double w1, out double c1);
                double d = (c0 + c1) / 2, sh = .66 + .34 * (d * .5 + .5);   // the lab shades to half: lifted, so the back never goes murky brown
                var (r, g, b) = MarblePal(t * .09 + (double)j / MarbleVaneCount * .55 + (y0 + y1) * .08 + seed * .013);
                var q = new SkinQuad((float)(x0 - w0), (float)y0, (float)(x0 + w0), (float)y0, (float)(x1 + w1), (float)y1,
                    (float)(x1 - w1), (float)y1, Argb(.9, r * sh, g * sh, b * sh));
                // Insertion by depth: the far side of a vane is drawn first.
                int k = n++;
                while (k > 0 && depth[k - 1] > d) { dst[k] = dst[k - 1]; depth[k] = depth[k - 1]; k--; }
                dst[k] = q; depth[k] = (float)d;
                y0 = y1; x0 = x1; w0 = w1; c0 = c1;
            }
        }
        return n;
    }

    private static void Vane(double y, int j, double tw, double ph, out double x, out double w, out double depth)
    {
        double en = Math.Sqrt(Math.Max(0, 1 - Math.Pow(y / .86, 2))), q = y * tw + ph + j * Tau / MarbleVaneCount;
        depth = Math.Cos(q);
        x = Math.Sin(q) * .4 * en;
        w = (Math.Abs(depth) * .16 + .02) * en;
    }

    /// <summary>The nine crack lines from the middle, five stops each, as x,y pairs (90 floats).</summary>
    public static void MarbleCracks(int seed, Span<float> xy)
    {
        ReadOnlySpan<double> stops = stackalloc double[] { 0, .3, .56, .78, .97 };
        for (int i = 0; i < MarbleCrackCount; i++)
        {
            double a = (double)i / MarbleCrackCount * Tau + Hash(seed, i) * .5;
            for (int s = 0; s < MarbleCrackStops; s++)
            {
                double aj = a + (s > 0 ? (Hash(seed + s, i) - .5) * .42 * (s < 4 ? 1 : .2) : 0);
                xy[(i * MarbleCrackStops + s) * 2] = (float)(Math.Cos(aj) * stops[s]);
                xy[(i * MarbleCrackStops + s) * 2 + 1] = (float)(Math.Sin(aj) * stops[s]);
            }
        }
    }

    /// <summary>The corners of shard <paramref name="index"/> (crack i between lines i and i+1, ring s), as x,y
    /// pairs into <paramref name="xy"/> (8 floats). Returns the corner count (3 at the middle, else 4).</summary>
    public static int MarbleShard(ReadOnlySpan<float> cracks, int index, Span<float> xy)
    {
        int i = index / 4, s = index % 4, d = (i + 1) % MarbleCrackCount;
        int P(int line, int stop) => (line * MarbleCrackStops + stop) * 2;
        Span<int> at = stackalloc int[4];
        int n;
        if (s == 0) { at[0] = P(i, 0); at[1] = P(i, 1); at[2] = P(d, 1); n = 3; }
        else { at[0] = P(i, s); at[1] = P(i, s + 1); at[2] = P(d, s + 1); at[3] = P(d, s); n = 4; }
        for (int k = 0; k < n; k++) { xy[k * 2] = cracks[at[k]]; xy[k * 2 + 1] = cracks[at[k] + 1]; }
        return n;
    }

    private static int MarbleBurst(int seed, double radiusPx, double motion, Span<SkinParticle> dst)
    {
        if (dst.Length < 1 + MarbleShards + MaxMarbleDust + MaxMarbleGlints || radiusPx <= 0 || motion <= 0) return 0;
        var rnd = new Mulberry((uint)seed);
        float m = (float)motion, minS = (float)(MinPx / radiusPx);
        int n = 0;
        dst[n++] = new SkinParticle(0, 0, 0, 0, .3f, 1f, SkinParticleKind.Wave, Grow: .8f, Argb: 0xD9FFFFFF);

        // The shards between the cracks fly out a little, then fall and turn.
        Span<float> cracks = stackalloc float[MarbleCrackCount * MarbleCrackStops * 2];
        Span<float> pc = stackalloc float[8];
        MarbleCracks(seed, cracks);
        for (int index = 0; index < MarbleShards; index++)
        {
            int k = MarbleShard(cracks, index, pc), s = index % 4;
            double cx = 0, cy = 0;
            for (int q = 0; q < k; q++) { cx += pc[q * 2]; cy += pc[q * 2 + 1]; }
            cx /= k; cy /= k;
            double len = Math.Max(1e-6, Math.Sqrt(cx * cx + cy * cy)), v = .9 + s * .7 + rnd.Next() * 1.3;
            var (r, g, b) = MarblePal(rnd.Next() * .5 + seed * .013);
            uint col = s < 3 && rnd.Next() < .4 ? Argb(.72, r, g, b) : MarbleGlassArgb;
            dst[n++] = new SkinParticle((float)cx, (float)cy, (float)(cx / len * v) * m, (float)(cy / len * v - 1.2) * m,
                (float)(.9 + rnd.Next() * .7), 1f, SkinParticleKind.Shard, Rise: 11f * m * m, Drag: .5f,
                Spin: (float)((rnd.Next() - .5) * 14) * m, Argb: col, Index: (ushort)index);
        }

        // Glass dust: fine, white, quick to fall.
        int dust = MarbleDustCount(radiusPx);
        for (int i = 0; i < dust; i++)
        {
            double an = rnd.Next() * Tau, v = 1.5 + rnd.Next() * 6;
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * .4), (float)(Math.Sin(an) * .4), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v - 1) * m, (float)(.4 + rnd.Next() * .6), Math.Max(minS, (float)(.008 + rnd.Next() * .014)),
                SkinParticleKind.Dot, Rise: 9f * m * m, Drag: 1.6f, Argb: MarbleWhiteArgb);
        }

        // A few four-point glints that twinkle as they go (Grow is the twinkle rate).
        int glints = MarbleGlintCount(radiusPx);
        for (int i = 0; i < glints; i++)
        {
            double an = rnd.Next() * Tau, v = 1 + rnd.Next() * 3;
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * .5), (float)(Math.Sin(an) * .5), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v) * m, (float)(.4 + rnd.Next() * .5), Math.Max(minS, (float)(.06 + rnd.Next() * .06)),
                SkinParticleKind.Star, Rise: 5f * m * m, Drag: 1.5f, Grow: (float)(14 + rnd.Next() * 10) * m, Argb: MarbleWhiteArgb);
        }
        return n;
    }

    private static uint Argb(double a, double r, double g, double b)
        => ((uint)Math.Clamp(Math.Round(a * 255), 0, 255) << 24) | ((uint)Math.Clamp(Math.Round(r), 0, 255) << 16)
         | ((uint)Math.Clamp(Math.Round(g), 0, 255) << 8) | (uint)Math.Clamp(Math.Round(b), 0, 255);
}
