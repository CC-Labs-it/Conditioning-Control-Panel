namespace ConditioningControlPanel.BubbleCore;

/// <summary>
/// The hues a Disco ball wears: its tiles, glints, thrown light and pop. Plain 0xRRGGBB values handed in by
/// the head (the desktop reads the active mod's accent and secondary); nothing here knows about mods.
/// </summary>
public sealed class DiscoPalette
{
    public const int MaxHues = 6;

    /// <summary>Pink, gold, cyan, lilac: the ball when no mod colours are known.</summary>
    public static readonly DiscoPalette Fallback = new(0xFF3D9E, 0xFFC229, 0x29D9FF, 0x9E6BFF);

    private readonly uint[] _rgb;

    public DiscoPalette(params uint[] rgb)
    {
        _rgb = rgb is { Length: > 0 } ? rgb.Take(MaxHues).Select(c => c & 0xFFFFFF).ToArray() : new uint[] { 0xFF4FA8 };
    }

    public int Count => _rgb.Length;

    /// <summary>A hue by any index (wraps both ways), 0xRRGGBB.</summary>
    public uint this[int i] => _rgb[((i % _rgb.Length) + _rgb.Length) % _rgb.Length];

    public bool SameAs(DiscoPalette? other) => other != null && _rgb.AsSpan().SequenceEqual(other._rgb);

    /// <summary>A hue for <paramref name="u"/> in 0..1. In a mod palette (five hues) the accent and the secondary
    /// come up twice as often as each neighbour, so the mod's own colours lead the party.</summary>
    public uint Pick(double u)
    {
        int n = _rgb.Length, slots = n >= 5 ? n + 2 : n, k = Math.Clamp((int)(u * slots), 0, slots - 1);
        return n >= 5 ? _rgb[k < 4 ? k / 2 : k - 2] : _rgb[k];
    }

    /// <summary>
    /// A party from a mod's two colours: the accent and the secondary as the main hues (lifted to at least a
    /// clear saturation), then three neighbours by hue shift (accent +45 deg, secondary -45 deg, accent +180 deg).
    /// A grey or missing colour is skipped; with neither, <see cref="Fallback"/>.
    /// </summary>
    public static DiscoPalette FromMod(uint? accent, uint? secondary)
    {
        var a = Usable(accent);
        var s = Usable(secondary);
        if (a == null && s == null) return Fallback;
        var main = a ?? s!.Value;
        (double H, double S, double V) second = a != null && s != null && HueGap(a.Value.H, s.Value.H) >= 20 ? s.Value : (main.H - 60, main.S, main.V);
        return new DiscoPalette(
            Rgb(main.H, Math.Max(main.S, .75), Math.Max(main.V, .92)),
            Rgb(second.H, Math.Max(second.S, .75), Math.Max(second.V, .92)),
            Rgb(main.H + 45, .85, .98),
            Rgb(second.H - 45, .85, .98),
            Rgb(main.H + 180, .8, .98));
    }

    /// <summary>"#RRGGBB" or "#AARRGGBB" (alpha dropped) to 0xRRGGBB.</summary>
    public static bool TryParseHex(string? hex, out uint rgb)
    {
        rgb = 0;
        var h = hex?.Trim().TrimStart('#');
        if (h == null || (h.Length != 6 && h.Length != 8)) return false;
        if (!uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
        rgb = v & 0xFFFFFF;
        return true;
    }

    private static (double H, double S, double V)? Usable(uint? rgb)
    {
        if (rgb == null) return null;
        var hsv = Hsv(rgb.Value);
        return hsv.S < .15 || hsv.V < .2 ? null : hsv;
    }

    private static double HueGap(double a, double b)
    {
        double d = Math.Abs(((a - b) % 360 + 360) % 360);
        return Math.Min(d, 360 - d);
    }

    internal static (double H, double S, double V) Hsv(uint rgb)
    {
        double r = ((rgb >> 16) & 255) / 255.0, g = ((rgb >> 8) & 255) / 255.0, b = (rgb & 255) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, h = 0;
        if (d > 1e-9)
            h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max <= 0 ? 0 : d / max, max);
    }

    internal static uint Rgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x) : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
        return ((uint)Math.Round((r + m) * 255) << 16) | ((uint)Math.Round((g + m) * 255) << 8) | (uint)Math.Round((b + m) * 255);
    }
}

/// <summary>
/// Disco: a mirror ball in place of the soap sprite. Every facet is a true patch of a sphere whose axis is
/// tipped toward the viewer, so facets narrow toward the edge and never reach past it. Each tile carries a
/// hue from the <see cref="DiscoPalette"/> (the mod's colours); a white light and a light that drifts through
/// the palette sweep glints across the tiles as the ball turns, and every tile swells and settles on its own
/// slow clock. Small star glints ride the tiles; light spots and thin rays are thrown off the ball. Nothing
/// switches on: every brightness is a smooth hump. The pop: a ring, palette beams, mirror shards in the palette
/// colours and glint stars. Under Gentle visuals the head passes the Reduced motion scale, which slows and softens.
/// </summary>
public static partial class BubbleSkins
{
    public const int MaxDiscoRows = 13, MaxDiscoCols = 28, MaxDiscoFacets = MaxDiscoRows * MaxDiscoCols;
    public const int MaxDiscoGlints = 7, MaxDiscoSpots = 14, MaxDiscoRays = 5;
    public const int DiscoBeams = 12, MaxDiscoSquares = 96, MaxDiscoStars = 16;
    /// <summary>The ball's radius as a share of the glass, and how far the axis is tipped toward the viewer.</summary>
    public const double DiscoBall = .98, DiscoTilt = .38, DiscoRoll = -.2;
    /// <summary>Thrown light (spots, rays) stays within this share of the glass (the sprite's own reach is 1 / 0.72).</summary>
    public const double DiscoFleckReach = 1.27;
    /// <summary>The ball's turn, radians per second at full motion.</summary>
    public const double DiscoSpin = .55;

    public static int DiscoRows(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .17), 7, MaxDiscoRows);
    public static int DiscoCols(double radiusPx) => Math.Min(MaxDiscoCols, DiscoRows(radiusPx) * 2 + 2);
    public static int DiscoGlintCount(double radiusPx) => radiusPx < 32 ? 4 : MaxDiscoGlints;
    public static int DiscoSpotCount(double radiusPx) => radiusPx < 32 ? 8 : MaxDiscoSpots;
    public static int DiscoRayCount(double radiusPx) => radiusPx < 32 ? 3 : MaxDiscoRays;
    public static int DiscoSquareCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * 1.2), 32, MaxDiscoSquares);
    public static int DiscoStarCount(double radiusPx) => Math.Clamp((int)Math.Round(radiusPx * .2), 6, MaxDiscoStars);

    /// <summary>The two lights as unit directions at motion-scaled time <paramref name="t"/>: a white key light and a
    /// coloured one. The lab's paths, normalised, so each light always has its highlight on the ball.</summary>
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

    private static double DiscoTurn(int seed, double t) => t * DiscoSpin + Hash(seed, 29) * Tau;

    /// <summary>The coloured light's hue: it drifts from one palette colour to the next and rests on each.</summary>
    private static uint DiscoDrift(DiscoPalette pal, int seed, double t)
    {
        double pos = t * .16 + Hash(seed, 37) * pal.Count;
        int i = (int)Math.Floor(pos);
        return Mix(pal[i], pal[i + 1], Smooth(.55, 1, pos - i));
    }

    /// <summary>A smooth hump 0..1 with a rest between humps: never a step, so a glint swells and settles.</summary>
    private static double Hump(double phase)
    {
        double s = Math.Sin(phase);
        return s <= 0 ? 0 : s * s * s;
    }

    /// <summary>The visible facets as quads in glass units (y down), each in its palette hue, lit by the two lights and
    /// its own slow shimmer. <paramref name="flare"/> (0..1, the pre-burst) lifts every tile toward its own hue,
    /// never to white. Far-side facets are skipped; a corner past the limb is pinned to it.</summary>
    public static int DiscoFacets(int seed, double seconds, double motion, double radiusPx, Span<SkinQuad> dst,
                                  DiscoPalette? palette = null, double flare = 0)
    {
        if (dst.Length < MaxDiscoFacets || radiusPx <= 0) return 0;
        var pal = palette ?? DiscoPalette.Fallback;
        motion = Math.Clamp(motion, 0, 1);
        double t = seconds * motion, amp = .55 + .45 * motion;   // Reduced (and Gentle) shimmer slower and softer
        int rows = DiscoRows(radiusPx), cols = DiscoCols(radiusPx), n = 0, sh = seed % 89;
        double rot = DiscoTurn(seed, t), dph = Math.PI / rows, dth = Tau / cols, gp = .08;
        var (l1, l2) = DiscoLights(seed, t);
        uint drift = DiscoDrift(pal, seed, t);
        double dr = (drift >> 16 & 255) / 255.0, dg = (drift >> 8 & 255) / 255.0, db = (drift & 255) / 255.0;
        flare = Math.Clamp(flare, 0, 1);
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
                double d1 = Math.Max(0, nx * l1.X + ny * l1.Y + nz * l1.Z), d2 = Math.Max(0, nx * l2.X + ny * l2.Y + nz * l2.Z);
                double s1 = Math.Pow(d1, 16), s2 = Math.Pow(d2, 12), w2 = Math.Pow(d2, 3) * .3;
                // The tile's own clock: it swells into its colour and settles, a few seconds apart.
                double tw = Hump(t * (1.1 + 1.3 * Hash(i + sh, j + 11)) + Hash(j + 3, i * 7 + sh) * Tau);
                tw *= tw * amp;
                uint hue = pal.Pick(Hash(i * 31 + sh, j * 17 + 5));
                double hr = (hue >> 16 & 255) / 255.0, hg = (hue >> 8 & 255) / 255.0, hb = (hue & 255) / 255.0;
                // An unlit tile is dark mirror (the room); colour comes only with light, so a dim warm hue never
                // turns to brown: the tile's own hue where a light or its shimmer catches it, the coloured light's wash.
                double k = Math.Pow(Math.Min(1, .1 + .14 * lam + .95 * tw + .85 * s1 + .55 * s2 + .5 * flare), 1.6);
                double white = .3 * s1 * s1 + .12 * tw * tw + .04 * flare;
                double lit = w2 + .35 * s2;
                double br = .085 + .1 * lam, bg = .08 + .09 * lam, bb = .13 + .15 * lam;   // the dark mirror body
                uint col = Argb(1, 255 * (br + hr * k + white + dr * lit), 255 * (bg + hg * k + white + dg * lit),
                                255 * (bb + hb * k + white + db * lit));
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

    /// <summary>
    /// The light the ball throws and its star glints, in glass units, each with its colour in Argb:
    /// <see cref="SkinPrimKind.Ray"/> (a thin ray from (X, Y) on the limb straight out, R long),
    /// <see cref="SkinPrimKind.Fleck"/> (a light spot circling the ball the way the ball turns),
    /// <see cref="SkinPrimKind.Glint"/> (a four-point star riding a tile, swelling and settling as it comes round).
    /// </summary>
    private static int DiscoInterior(int seed, double seconds, double motion, double radiusPx, Span<SkinPrim> dst, DiscoPalette? palette)
    {
        if (dst.Length < MaxDiscoGlints + MaxDiscoSpots + MaxDiscoRays || radiusPx <= 0) return 0;
        var pal = palette ?? DiscoPalette.Fallback;
        motion = Math.Clamp(motion, 0, 1);
        double t = seconds * motion, amp = .55 + .45 * motion, minR = MinPx / radiusPx, rot = DiscoTurn(seed, t);
        int n = 0;

        int rays = DiscoRayCount(radiusPx);
        for (int m = 0; m < rays; m++)
        {
            double an = Hash(seed + 51, m) * Tau + rot * .5, len = .16 + .1 * Hash(seed + 53, m);
            double al = (.1 + .45 * Hump(t * (.7 + .5 * Hash(seed + 57, m)) + Hash(seed + 59, m) * Tau)) * amp;
            double r0 = DiscoBall - .02;
            dst[n++] = new SkinPrim((float)(Math.Cos(an) * r0), (float)(Math.Sin(an) * r0), (float)len, (float)al, SkinPrimKind.Ray,
                                    Argb: pal[m + 1] | 0xFF000000);
        }

        int spots = DiscoSpotCount(radiusPx);
        for (int m = 0; m < spots; m++)
        {
            double an = Hash(seed + 61, m) * Tau + rot, rr = 1.05 + .17 * Hash(seed + 67, m);
            double s = Math.Max(minR, .018 + .016 * Hash(seed + 71, m));
            double al = .1 + .55 * Hump(t * (.9 + .9 * Hash(seed + 73, m)) + Hash(seed + 79, m) * Tau) * amp;
            dst[n++] = new SkinPrim((float)(Math.Cos(an) * rr), (float)(Math.Sin(an) * rr), (float)s, (float)al, SkinPrimKind.Fleck,
                                    Argb: Mix(pal[m], 0xFFFFFF, .25) | 0xFF000000);
        }

        int glints = DiscoGlintCount(radiusPx);
        for (int g = 0; g < glints; g++)
        {
            double lat = Math.Asin(Hash(seed + 81, g) * 1.6 - .8), lon = Hash(seed + 83, g) * Tau + rot;
            var p = DiscoPoint(lat, lon);
            double face = Smooth(.15, .65, p.Z);
            double life = Hump(t * (.6 + .5 * Hash(seed + 87, g)) + Hash(seed + 89, g) * Tau);
            double al = Math.Clamp(life * face * (.45 + .55 * amp), 0, 1);
            double s = Math.Max(minR, (.1 + .08 * Hash(seed + 91, g)) * (.45 + .55 * life));
            // The star sits on the tile; its arms never reach past the ball.
            double x = p.X * DiscoBall, y = p.Y * DiscoBall, max = DiscoBall - s, d = Math.Sqrt(x * x + y * y);
            if (d > max && d > 1e-9) { x *= Math.Max(0, max) / d; y *= Math.Max(0, max) / d; }
            uint col = g % 3 == 0 ? 0xFFFFFF : Mix(pal[g], 0xFFFFFF, .45);
            dst[n++] = new SkinPrim((float)x, (float)y, (float)s, (float)al, SkinPrimKind.Glint, Argb: col | 0xFF000000);
        }
        return n;
    }

    private static int DiscoBurst(int seed, double radiusPx, double motion, bool gentle, Span<SkinParticle> dst, DiscoPalette? palette)
    {
        if (dst.Length < 1 + DiscoBeams + MaxDiscoSquares + MaxDiscoStars || radiusPx <= 0 || motion <= 0) return 0;
        var pal = palette ?? DiscoPalette.Fallback;
        var rnd = new Mulberry((uint)seed);
        float m = (float)motion, minS = (float)(MinPx / radiusPx);
        int n = 0;
        dst[n++] = new SkinParticle(0, 0, 0, 0, .32f, 1f, SkinParticleKind.Wave, Grow: 1f, Argb: Mix(pal[0], 0xFFFFFF, .5) | 0xCC000000);

        // Thin beams in the palette that sweep a little as they fade. Under Gentle visuals they would flash: none.
        for (int i = 0; i < DiscoBeams; i++)
        {
            float rot = (float)(i / (double)DiscoBeams * Tau + rnd.Next());
            if (gentle) continue;
            dst[n++] = new SkinParticle(0, 0, 0, 0, .5f, 2.8f, SkinParticleKind.Beam, Rot: rot, Spin: 1.6f * m,
                                        Argb: (i % 3 == 0 ? 0xFFFFFF : pal[i]) | 0xE6000000);
        }

        // Mirror shards in the palette that tumble and catch the light (Grow: how fast; 0 under Gentle: a steady tilt).
        int squares = DiscoSquareCount(radiusPx);
        for (int i = 0; i < squares; i++)
        {
            double an = rnd.Next() * Tau, v = 1.5 + rnd.Next() * 5.5, d = rnd.Next() * .8;
            float life = (float)(.7 + rnd.Next() * .8), size = Math.Max(minS, (float)(.028 + rnd.Next() * .035));
            float rot = (float)(rnd.Next() * 6), spin = (float)((rnd.Next() - .5) * 18) * m;
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * d), (float)(Math.Sin(an) * d), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v - 1) * m, life, size, SkinParticleKind.Square, Rise: 5f * m * m, Drag: 1.9f,
                Grow: gentle ? 0 : 1.7f, Rot: rot, Spin: gentle ? spin * .3f : spin, Argb: (i < pal.Count ? pal[i] : pal.Pick(rnd.Next())) | 0xFF000000);
        }

        // Glint stars, white and palette-light; they twinkle slowly under Gentle visuals.
        int stars = DiscoStarCount(radiusPx);
        for (int i = 0; i < stars; i++)
        {
            double an = rnd.Next() * Tau, v = 1 + rnd.Next() * 4;
            float life = (float)(.4 + rnd.Next() * .6), size = Math.Max(minS, (float)(.07 + rnd.Next() * .08));
            float tw = (float)(12 + rnd.Next() * 12);
            dst[n++] = new SkinParticle((float)(Math.Cos(an) * .6), (float)(Math.Sin(an) * .6), (float)(Math.Cos(an) * v) * m,
                (float)(Math.Sin(an) * v) * m, life, size, SkinParticleKind.Star, Rise: 2f * m * m, Drag: 2f,
                Grow: (gentle ? 2.5f : tw) * m, Argb: (i % 3 == 0 ? 0xFFFFFF : Mix(pal[i], 0xFFFFFF, .4)) | 0xF2000000);
        }
        return n;
    }

    /// <summary>Two 0xRRGGBB colours mixed, k = 0 the first, 1 the second.</summary>
    private static uint Mix(uint a, uint b, double k)
    {
        uint Ch(int sh) => (uint)Math.Round(Lerp((a >> sh) & 255, (b >> sh) & 255, Math.Clamp(k, 0, 1)));
        return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    private static double Smooth(double a, double b, double x)
    {
        double k = Math.Clamp((x - a) / (b - a), 0, 1);
        return k * k * (3 - 2 * k);
    }
}
