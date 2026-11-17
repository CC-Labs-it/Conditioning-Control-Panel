namespace ConditioningControlPanel.BubbleCore;

/// <summary>
/// Disco: a mirror ball in place of the soap sprite. Every facet is a true patch of a sphere whose axis is
/// tipped toward the viewer, so facets narrow toward the edge and never reach past it. Two slowly moving
/// lights (white, pink) light it with a little per-facet variation; at most two glints at a time, kept
/// apart and faded in and out over seconds, never switched; small flecks of light orbit outside. The pop
/// is a ring, thin beams, many small mirror squares and a few four-point stars. Ported from the bubble lab.
/// </summary>
public static partial class BubbleSkins
{
    public const int MaxDiscoRows = 13, MaxDiscoCols = 28, MaxDiscoFacets = MaxDiscoRows * MaxDiscoCols;
    public const int DiscoGlints = 2, MaxDiscoFlecks = 8;
    public const int DiscoBeams = 12, MaxDiscoSquares = 96, MaxDiscoStars = 16;
    /// <summary>The ball's radius as a share of the glass, and how far the axis is tipped toward the viewer.</summary>
    public const double DiscoBall = .98, DiscoTilt = .38, DiscoRoll = -.2;
    /// <summary>Flecks orbit out to this share of the glass (the sprite's own reach is 1 / 0.72).</summary>
    public const double DiscoFleckReach = 1.27;

    public static int DiscoRows(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .17), 7, MaxDiscoRows);
    public static int DiscoCols(double radiusPx) => Math.Min(MaxDiscoCols, DiscoRows(radiusPx) * 2 + 2);
    public static int DiscoFleckCount(double radiusPx) => radiusPx < 32 ? 6 : MaxDiscoFlecks;
    public static int DiscoSquareCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 1.2), 32, MaxDiscoSquares);
    public static int DiscoStarCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .2), 6, MaxDiscoStars);

    public const uint DiscoMirrorArgb = 0xFFB9B6C9, DiscoPinkArgb = 0xFFFF4FA8, DiscoWhiteArgb = 0xF2FFFFFF, DiscoGlowArgb = 0xF2FF96CD;

    /// <summary>The two lights (white, pink) as unit directions at motion-scaled time <paramref name="t"/>. The lab's paths,
    /// normalised, so each light always has its highlight on the ball.</summary>
    private static ((double X, double Y, double Z) L1, (double X, double Y, double Z) L2) DiscoLights(int seed, double t)
    {
        double s = Hash(seed, 31) * Tau;
        return (Unit(Math.Cos(t * .9 + s) * .62, Math.Sin(t * .7) * .45 - .3, .68),
                Unit(-Math.Cos(t * .6 + 2 + s) * .6, Math.Sin(t * .8 + 1) * .5, .62));
    }

    private static (double X, double Y, double Z) Unit(double x, double y, double z)
    {
        double n = Math.Sqrt(x * x + y * y + z * z);
        return (x / n, y / n, z / n);
    }

    /// <summary>A point of the unit sphere (latitude, longitude; y down, z toward the viewer) after the tilt and the roll.</summary>
    private static (double X, double Y, double Z) DiscoPoint(double lat, double lon)
    {
        double x = Math.Cos(lat) * Math.Sin(lon), y = Math.Sin(lat), z = Math.Cos(lat) * Math.Cos(lon);
        double ct = Math.Cos(DiscoTilt), st = Math.Sin(DiscoTilt);
        double y1 = y * ct + z * st, z1 = -y * st + z * ct;   // the top pole tips toward the viewer
        double cr = Math.Cos(DiscoRoll), sr = Math.Sin(DiscoRoll);
        return (x * cr - y1 * sr, x * sr + y1 * cr, z1);
    }

    /// <summary>The visible facets as quads in glass units (y down), coloured by the two lights. Facets on the far
    /// side are skipped; a corner past the limb is pinned to it, so every corner sits within <see cref="DiscoBall"/>.</summary>
    public static int DiscoFacets(int seed, double seconds, double motion, double radiusPx, Span<SkinQuad> dst)
    {
        if (dst.Length < MaxDiscoFacets || radiusPx <= 0) return 0;
        double t = seconds * Math.Clamp(motion, 0, 1);
        int rows = DiscoRows(radiusPx), cols = DiscoCols(radiusPx), n = 0, sh = seed % 89;
        double rot = t * .55 + Hash(seed, 29) * Tau, dph = Math.PI / rows, dth = Tau / cols, gp = .08;
        var (l1, l2) = DiscoLights(seed, t);
        for (int i = 0; i < rows; i++)
        {
            double pa = -Math.PI / 2 + (i + gp) * dph, pb = -Math.PI / 2 + (i + 1 - gp) * dph, pm = (pa + pb) / 2;
            for (int j = 0; j < cols; j++)
            {
                double th = j * dth + rot + (i % 2) * dth * .5, ta = th + dth * gp, tb = th + dth * (1 - gp), tm = (ta + tb) / 2;
                var c = DiscoPoint(pm, tm);
                if (c.Z <= .02) continue;
                var p0 = Limb(DiscoPoint(pa, ta)); var p1 = Limb(DiscoPoint(pa, tb));
                var p2 = Limb(DiscoPoint(pb, tb)); var p3 = Limb(DiscoPoint(pb, ta));
                // A little per-facet variation of the normal, so the mirrors do not light in lockstep.
                double nx = c.X + (Hash(i * 53 + sh, j) - .5) * .22, ny = c.Y + (Hash(j + 7, i + sh) - .5) * .22, nz = c.Z;
                double nn = Math.Sqrt(nx * nx + ny * ny + nz * nz); nx /= nn; ny /= nn; nz /= nn;
                double lam = Math.Max(0, -.45 * nx - .55 * ny + .7 * nz);
                // Each light: a tight mirror highlight (the lit facets) and a broad wash of its colour.
                double d1 = Math.Max(0, nx * l1.X + ny * l1.Y + nz * l1.Z), d2 = Math.Max(0, nx * l2.X + ny * l2.Y + nz * l2.Z);
                double s1 = Math.Pow(d1, 22), s2 = Math.Pow(d2, 18), w1 = Math.Pow(d1, 4) * .5, w2 = Math.Pow(d2, 4) * .6;
                uint col = Argb(1, 20 + 62 * lam + 255 * s1 + 255 * s2 + 50 * w1 + 90 * w2, 20 + 62 * lam + 255 * s1 + 70 * s2 + 50 * w1 + 22 * w2,
                                28 + 78 * lam + 255 * s1 + 170 * s2 + 62 * w1 + 60 * w2);
                float b = (float)DiscoBall;
                dst[n++] = new SkinQuad((float)p0.X * b, (float)p0.Y * b, (float)p1.X * b, (float)p1.Y * b,
                                        (float)p2.X * b, (float)p2.Y * b, (float)p3.X * b, (float)p3.Y * b, col);
            }
        }
        return n;
    }

    private static (double X, double Y) Limb((double X, double Y, double Z) p)
    {
        if (p.Z >= 0) return (p.X, p.Y);
        double d = Math.Sqrt(p.X * p.X + p.Y * p.Y);
        return d > 1e-9 ? (p.X / d, p.Y / d) : (p.X, p.Y);
    }

    /// <summary>The glints (Glint white, GlintPink) and the orbiting flecks (Fleck, FleckPink) in glass units.
    /// A glint sits where its light's highlight falls on the ball; it swells and fades over seconds, and the
    /// weaker of two that come close fades away, so nothing ever switches on.</summary>
    private static int DiscoInterior(int seed, double seconds, double motion, double radiusPx, Span<SkinPrim> dst)
    {
        if (dst.Length < DiscoGlints + MaxDiscoFlecks || radiusPx <= 0) return 0;
        double t = seconds * Math.Clamp(motion, 0, 1), minR = MinPx / radiusPx, ph = Hash(seed, 33) * Tau;
        var (l1, l2) = DiscoLights(seed, t);
        Span<double> gx = stackalloc double[2], gy = stackalloc double[2], gs = stackalloc double[2];
        for (int k = 0; k < 2; k++)
        {
            var l = k == 0 ? l1 : l2;
            double ln = Math.Sqrt(l.X * l.X + l.Y * l.Y + l.Z * l.Z), hx = l.X / ln, hy = l.Y / ln, hz = l.Z / ln + 1;
            double hn = Math.Sqrt(hx * hx + hy * hy + hz * hz);
            gx[k] = hx / hn * DiscoBall; gy[k] = hy / hn * DiscoBall;
            double come = Smooth(.2, .8, .5 + .5 * Math.Sin(t * .8 + k * Math.PI + ph));
            gs[k] = come * (.8 + .2 * Math.Sin(t * 1.3 + k * 2 + ph)) * Smooth(.15, .5, hz / hn);
        }
        double d = Math.Sqrt((gx[0] - gx[1]) * (gx[0] - gx[1]) + (gy[0] - gy[1]) * (gy[0] - gy[1]));
        // Never piled together: as the two come close the weaker one fades (a soft choice, so nothing jumps).
        double apart = Smooth(.25, .65, d), weak0 = Smooth(-.3, .3, gs[1] - gs[0]);
        double g0 = gs[0] * Lerp(1, apart, weak0), g1 = gs[1] * Lerp(1, apart, 1 - weak0);
        gs[0] = g0; gs[1] = g1;
        int n = 0;
        for (int k = 0; k < 2; k++)
            dst[n++] = new SkinPrim((float)gx[k], (float)gy[k], (float)Math.Max(minR, .17 * gs[k]), (float)Math.Clamp(gs[k] * .95, 0, 1),
                                    k == 0 ? SkinPrimKind.Glint : SkinPrimKind.GlintPink);
        int flecks = DiscoFleckCount(radiusPx);
        for (int m = 0; m < flecks; m++)
        {
            double an = t * .5 * (m % 2 == 1 ? 1 : -1) + m * .8 + ph, rr = 1.1 + .14 * Hash(seed, m);
            double al = .16 + .12 * Math.Sin(t * 2 + m);
            dst[n++] = new SkinPrim((float)(Math.Cos(an) * rr), (float)(Math.Sin(an) * rr), (float)Math.Max(minR, .028), (float)al,
                                    m % 2 == 1 ? SkinPrimKind.FleckPink : SkinPrimKind.Fleck);
        }
        return n;
    }

    private static int DiscoBurst(int seed, double radiusPx, double motion, bool gentle, Span<SkinParticle> dst)
    {
        if (dst.Length < 1 + DiscoBeams + MaxDiscoSquares + MaxDiscoStars || radiusPx <= 0 || motion <= 0) return 0;
        var rnd = new Mulberry((uint)seed);
        float m = (float)motion, minS = (float)(MinPx / radiusPx);
        int n = 0;
        dst[n++] = new SkinParticle(0, 0, 0, 0, .32f, 1f, SkinParticleKind.Wave, Grow: 1f, Argb: 0xCCFFFFFF);

        // Thin beams that sweep a little as they fade. Under Gentle visuals they would flash: none.
        for (int i = 0; i < DiscoBeams; i++)
        {
            float rot = (float)(i / (double)DiscoBeams * Tau + rnd.Next());
            if (gentle) continue;
            dst[n++] = new SkinParticle(0, 0, 0, 0, .5f, 2.8f, SkinParticleKind.Beam, Rot: rot, Spin: 1.6f * m,
                                        Argb: i % 2 == 1 ? 0xE6FF5AAA : 0xE6FFFFFF);
        }

        // Mirror squares that tumble and catch the light (Grow is that flicker; 0 under Gentle: a steady tilt, never white).
        int squares = DiscoSquareCount(radiusPx);
        for (int i = 0; i < squares; i++)
        {
            double an = rnd.Next() * Tau, v = 1.5 + rnd.Next() * 5.5, d = rnd.Next() * .8;
            float life = (float)(.7 + rnd.Next() * .8), size = Math.Max(minS, (float)(.028 + rnd.Next() * .035));
            float rot = (float)(rnd.Next() * 6), spin = (float)((rnd.Next() - .5) * 18) * m;
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * d), (float)(Math.Sin(an) * d), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v - 1) * m, life, size, SkinParticleKind.Square, Rise: 5f * m * m, Drag: 1.9f,
                Grow: gentle ? 0 : 1.7f, Rot: rot, Spin: gentle ? spin * .3f : spin, Argb: i % 4 != 0 ? DiscoMirrorArgb : DiscoPinkArgb);
        }

        // A few four-point stars; they twinkle slowly under Gentle visuals.
        int stars = DiscoStarCount(radiusPx);
        for (int i = 0; i < stars; i++)
        {
            double an = rnd.Next() * Tau, v = 1 + rnd.Next() * 4;
            float life = (float)(.4 + rnd.Next() * .6), size = Math.Max(minS, (float)(.07 + rnd.Next() * .08));
            float tw = (float)(12 + rnd.Next() * 12);
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * .6), (float)(Math.Sin(an) * .6), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v) * m, life, size, SkinParticleKind.Star, Rise: 2f * m * m, Drag: 2f,
                Grow: (gentle ? 2.5f : tw) * m, Argb: i % 3 != 0 ? DiscoWhiteArgb : DiscoGlowArgb);
        }
        return n;
    }

    private static double Smooth(double a, double b, double x)
    {
        double k = Math.Clamp((x - a) / (b - a), 0, 1);
        return k * k * (3 - 2 * k);
    }
}
