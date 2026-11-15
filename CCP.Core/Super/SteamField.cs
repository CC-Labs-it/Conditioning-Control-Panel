namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Steam: the pink fog as a low-resolution density grid for ONE monitor. The fog creeps in from
/// the edges toward a level, the cursor wipes it, pops and breaks punch holes in it, the bouncing
/// word cuts a trail, and the Whirlpool winds it into arms. Everything that is not the creep heals
/// back slowly, which is what makes a swirl linger after the pool has moved on.
/// Coordinates in are device pixels local to the monitor (0,0 = its top-left).
/// No WPF, no Skia: the head reads <see cref="Fog"/> and <see cref="Mem"/> and draws them.
/// </summary>
public sealed class SteamField
{
    /// <summary>Grid cells across. Rows follow the monitor's shape.</summary>
    public const int Columns = 160;
    /// <summary>How fast the fog closes back over a hole or a swirl (1/s). Slow on purpose.</summary>
    public const double HealRate = 0.22;
    /// <summary>How fast a wipe clears (1/s at the centre of the wipe).</summary>
    public const double WipeRate = 14;
    /// <summary>The eye of the pool, as a share of its reach, stays clear.</summary>
    public const double EyeShare = 0.16;

    public readonly int W, H;
    public readonly double WidthPx, HeightPx;
    /// <summary>Density 0..1 per cell, row by row.</summary>
    public readonly float[] Fog;
    /// <summary>Where the fog has been wiped at least once: words show there when it returns.</summary>
    public readonly float[] Mem;
    private readonly float[] _scratch;
    /// <summary>True when a step changed something worth a repaint.</summary>
    public bool Dirty { get; private set; } = true;

    public SteamField(double widthPx, double heightPx)
    {
        WidthPx = Math.Max(1, widthPx); HeightPx = Math.Max(1, heightPx);
        W = Columns; H = Math.Max(8, (int)Math.Round(Columns * HeightPx / WidthPx));
        Fog = new float[W * H]; Mem = new float[W * H]; _scratch = new float[W * H];
    }

    public void ClearDirty() => Dirty = false;
    private double Cx(double xPx) => xPx / WidthPx * W;
    private double Cy(double yPx) => yPx / HeightPx * H;
    private double Cells(double px) => px / WidthPx * W;

    /// <summary>
    /// Where the fog wants to be in a cell: thick at the edges, reaching the middle as
    /// <paramref name="level"/> climbs from 0 to about 1.5. <paramref name="cap"/> is the most it may cover.
    /// </summary>
    public double Target(int gx, int gy, double level, double cap)
    {
        double d = Math.Min(Math.Min(gx, W - 1 - gx), Math.Min(gy, H - 1 - gy)) / (H / 2.0);
        return Math.Clamp((level - d) * 2.6, 0, 1) * cap;
    }

    /// <summary>The creep and the heal. Call once a frame.</summary>
    public void Step(double dt, double level, double cap)
    {
        if (dt <= 0) return;
        float k = (float)(1 - Math.Exp(-HealRate * dt));
        bool moved = false;
        for (int gy = 0, i = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++, i++)
            {
                float t = (float)Target(gx, gy, level, cap), f = Fog[i], n = f + (t - f) * k;
                if (Math.Abs(n - f) > 0.0004f) moved = true;
                Fog[i] = n;
            }
        if (moved) Dirty = true;
    }

    /// <summary>Start (or restart) with the fog already at its creep for this level.</summary>
    public void Settle(double level, double cap)
    {
        for (int gy = 0, i = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++, i++) Fog[i] = (float)Target(gx, gy, level, cap);
        Array.Clear(Mem);
        Dirty = true;
    }

    /// <summary>A soft wipe that takes time (the cursor, the bouncing word). <paramref name="mark"/>
    /// = remember the spot so words come back in the fog there.</summary>
    public void Wipe(double xPx, double yPx, double radiusPx, double dt, bool mark)
    {
        double cx = Cx(xPx), cy = Cy(yPx), r = Math.Max(1, Cells(radiusPx));
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(W - 1, (int)(cx + r) + 1);
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(H - 1, (int)(cy + r) + 1);
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double d = Math.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
                if (d >= r) continue;
                double w = 1 - d / r; int i = gy * W + gx;
                Fog[i] *= (float)(1 - Math.Clamp(dt * WipeRate * w, 0, 1));
                if (mark) Mem[i] = Math.Max(Mem[i], (float)Math.Min(1, w * 4));
            }
        Dirty = true;
    }

    /// <summary>An instant soft hole (a pop, a break, a gold corner). Fades at its rim.</summary>
    public void Hole(double xPx, double yPx, double radiusPx)
    {
        double cx = Cx(xPx), cy = Cy(yPx), r = Math.Max(1.5, Cells(radiusPx));
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(W - 1, (int)(cx + r) + 1);
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(H - 1, (int)(cy + r) + 1);
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double d = Math.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
                if (d >= r) continue;
                Fog[gy * W + gx] *= (float)(1 - Math.Clamp((1 - d / r) * 2.2, 0, 1));
            }
        Dirty = true;
    }

    /// <summary>Mark a band so the next fog over it carries words (a finished Afterglow line).</summary>
    public void Remember(double x0Px, double y0Px, double x1Px, double y1Px)
    {
        int a = Math.Clamp((int)Cx(x0Px), 0, W - 1), b = Math.Clamp((int)Cx(x1Px), 0, W - 1);
        int c = Math.Clamp((int)Cy(y0Px), 0, H - 1), d = Math.Clamp((int)Cy(y1Px), 0, H - 1);
        for (int gy = c; gy <= d; gy++) for (int gx = a; gx <= b; gx++) Mem[gy * W + gx] = 1;
        Dirty = true;
    }

    /// <summary>
    /// The pool winds the fog. Inside its reach the density is carried around the centre (faster
    /// near the eye) and a little inward, the eye is kept clear, and the fog is thickened along
    /// <paramref name="arms"/> spiral bands and thinned between them. Nothing here undoes itself:
    /// when the pool leaves, the arms it wound stay and <see cref="Step"/> heals them slowly.
    /// </summary>
    public void Swirl(double xPx, double yPx, double radiusPx, double eased, double angle, double spin, double dt, double cap, int arms = 3)
    {
        if (dt <= 0 || eased <= 0.01 || radiusPx <= 1) return;
        double cx = Cx(xPx), cy = Cy(yPx), r = Cells(radiusPx);
        // The grid is not square in pixels when the monitor is not 160:H; work in cell units, the
        // error is a slightly oval swirl and nobody can see it.
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(W - 1, (int)(cx + r) + 1);
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(H - 1, (int)(cy + r) + 1);
        if (x0 > x1 || y0 > y1) return;
        Array.Copy(Fog, _scratch, Fog.Length);
        double eye = r * EyeShare, turns = 1.6 + eased * 3.4, pitch = r / (turns * 2 * Math.PI);
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double dx = gx - cx, dy = gy - cy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= r) continue;
                int i = gy * W + gx;
                double edge = 1 - d / r;                      // 0 at the rim, 1 at the centre
                double soft = Math.Min(1, edge * 4);          // the reach fades in over its outer quarter
                // Carry: look back along the turn and a little further out.
                // The fog turns WITH the arms (same way, same rate), so what the arms carve stays
                // carved instead of smearing: that is what makes them read as a fog spiral.
                double w = spin * dt;
                double cs = Math.Cos(w), sn = Math.Sin(w), back = 1 + 0.25 * eased * dt * edge;
                double sx = cx + (dx * cs - dy * sn) * back, sy = cy + (dx * sn + dy * cs) * back;
                double carried = Sample(_scratch, sx, sy);
                double f = Fog[i] + (carried - Fog[i]) * soft;
                // Arms: thicker on the band, thinner between, turning with the drawn spiral.
                double band = Math.Sin(Math.PI * Math.Min(1, d / r)) * eased;
                double arm = 0.5 - 0.5 * Math.Cos(arms * Math.Atan2(dy, dx) - arms * d / Math.Max(0.5, pitch) + arms * angle);
                f += (arm - 0.5) * band * 3.2 * dt * cap;
                if (d < eye) f *= Math.Clamp(1 - (1 - d / eye) * 6 * dt * (0.4 + eased), 0, 1);
                Fog[i] = (float)Math.Clamp(f, 0, 1);
            }
        Dirty = true;
    }

    private double Sample(float[] a, double x, double y)
    {
        x = Math.Clamp(x, 0, W - 1.001); y = Math.Clamp(y, 0, H - 1.001);
        int ix = (int)x, iy = (int)y; double fx = x - ix, fy = y - iy; int i = iy * W + ix;
        return a[i] * (1 - fx) * (1 - fy) + a[i + 1] * fx * (1 - fy) + a[i + W] * (1 - fx) * fy + a[i + W + 1] * fx * fy;
    }

    /// <summary>Density at a pixel, 0..1 (tests, and effects that want to know if they are in fog).</summary>
    public double At(double xPx, double yPx) => Sample(Fog, Cx(xPx), Cy(yPx));
}
