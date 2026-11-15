namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Steam: the pink fog as a low-resolution density grid for ONE monitor. The fog creeps in from
/// the edges toward a level, the cursor wipes it, pops and breaks punch holes in it, the bouncing
/// word cuts a trail, and the Whirlpool winds it into arms. Everything that is not the creep heals
/// back slowly, which is what makes a swirl linger after the pool has moved on.
/// The fog also MOVES: every cell has a velocity (<see cref="Vx"/>, <see cref="Vy"/>) that the
/// cursor, the pool and the pops feed, that carries the density along and coasts to a stop, on
/// top of a slow billow that is always there. Density is carried, not erased: what a moving
/// cursor clears is shoved ahead and aside, so a cell may hold more than 1 for a while.
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
    /// <summary>The fastest the fog itself may travel, in cells a second (about 310 px/s on a 1920 monitor).</summary>
    public const double MaxSpeed = 26;
    /// <summary>How long pushed or wound fog keeps going (s to fall to about a third).</summary>
    public const double MomentumTime = 1.8;
    /// <summary>The speed the pool leaves behind at the rim of the fog it wound, in cells a second.</summary>
    public const double PoolMomentum = 14;
    /// <summary>The most a cell may hold where pushed fog piles up.</summary>
    public const double MaxPile = 1.6;
    /// <summary>The cursor shoves all it clears once it travels this many wipe radii a second.</summary>
    public const double ShoveFullAt = 9;
    /// <summary>Share of the cleared fog a full shove keeps (the rest is gone).</summary>
    public const double ShoveKeep = 0.9;
    /// <summary>How fast a pop throws the fog outward at its rim, in cells a second.</summary>
    public const double PuffSpeed = 15;
    /// <summary>How fast the texture's drag springs back (s).</summary>
    public const double ShiftTime = 1.3;

    public readonly int W, H;
    public readonly double WidthPx, HeightPx;
    /// <summary>Density per cell, row by row: 0..1 at rest, up to <see cref="MaxPile"/> where pushed fog piles up.</summary>
    public readonly float[] Fog;
    /// <summary>Where the fog has been wiped at least once: words show there when it returns.</summary>
    public readonly float[] Mem;
    /// <summary>The fog's own velocity per cell, in cells a second. Fed by pushes, the pool and pops; it decays.</summary>
    public readonly float[] Vx, Vy;
    /// <summary>How far the moving fog has dragged its texture, in cells. The head offsets its cloud
    /// pattern by this so wisps are seen to be carried; it springs back to 0.</summary>
    public readonly float[] ShiftX, ShiftY;
    private readonly float[] _scratch, _ux, _uy, _hold, _psi, _edge;
    private bool _active, _held;
    private double _time;
    private int _psiRow;
    // The billow: a few standing swirls whose strengths swell and fade out of step. (m, n) = half
    // waves across and down, then speed in cells/s, period in s, phase.
    private static readonly (int M, int N, double Speed, double Period, double Phase)[] Modes =
    {
        (2, 1, 1.3, 47, 0.6), (3, 2, 1.5, 31, 2.1), (1, 2, 1.0, 59, 4.0), (5, 3, 1.4, 23, 5.2), (4, 2, 1.2, 37, 1.3),
    };
    private readonly float[][] _mx, _my;
    private readonly double[] _amp = new double[Modes.Length];
    /// <summary>The heal rate in use (1/s), set from the player's "Fog returns" (<see cref="SteamOptions.HealRate"/>).</summary>
    public double Heal { get; set; } = HealRate;
    /// <summary>Share of the stock billow, set from the player's "Drift" (<see cref="SteamOptions.Drift"/>). 0 = still.</summary>
    public double Drift { get; set; } = 1;
    /// <summary>True when a step changed something worth a repaint.</summary>
    public bool Dirty { get; private set; } = true;

    public SteamField(double widthPx, double heightPx)
    {
        WidthPx = Math.Max(1, widthPx); HeightPx = Math.Max(1, heightPx);
        W = Columns; H = Math.Max(8, (int)Math.Round(Columns * HeightPx / WidthPx));
        int n = W * H;
        Fog = new float[n]; Mem = new float[n]; _scratch = new float[n];
        Vx = new float[n]; Vy = new float[n]; ShiftX = new float[n]; ShiftY = new float[n];
        _ux = new float[n]; _uy = new float[n]; _hold = new float[n];
        _psi = new float[(W + 1) * (H + 1)];
        // How far each cell is from the nearest edge, as Target measures it.
        _edge = new float[n];
        for (int gy = 0, i = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++, i++)
                _edge[i] = (float)(Math.Min(Math.Min(gx, W - 1 - gx), Math.Min(gy, H - 1 - gy)) / (H / 2.0));
        _mx = new float[Modes.Length][]; _my = new float[Modes.Length][];
        for (int k = 0; k < Modes.Length; k++)
        {
            var m = Modes[k];
            _mx[k] = new float[W + 1]; _my[k] = new float[H + 1];
            for (int x = 0; x <= W; x++) _mx[k][x] = (float)Math.Sin(Math.PI * m.M * x / W);
            for (int y = 0; y <= H; y++) _my[k][y] = (float)Math.Sin(Math.PI * m.N * y / H);
            // Stream function height for the wanted speed (speed = its steepest slope).
            _amp[k] = m.Speed / (Math.PI * Math.Max(m.M / (double)W, m.N / (double)H));
        }
        Billow(H + 1);
    }

    /// <summary>
    /// Refresh some rows of the billow's stream function (kept at the cell corners, zero on the
    /// monitor's edge). Velocities are its differences, so the billow never squeezes the fog and
    /// never blows through the edge. It changes over tens of seconds: a few rows a step is plenty.
    /// </summary>
    private void Billow(int rows)
    {
        Span<float> a = stackalloc float[Modes.Length];
        for (int k = 0; k < Modes.Length; k++)
            a[k] = (float)(_amp[k] * Math.Sin(_time * 2 * Math.PI / Modes[k].Period + Modes[k].Phase));
        int n1 = W + 1;
        for (int r = 0; r < rows; r++)
        {
            int y = _psiRow; _psiRow = (_psiRow + 1) % (H + 1);
            int o = y * n1;
            for (int x = 0; x <= W; x++) _psi[o + x] = 0;
            for (int k = 0; k < Modes.Length; k++)
            {
                float s = a[k] * _my[k][y]; var mx = _mx[k];
                for (int x = 0; x <= W; x++) _psi[o + x] += s * mx[x];
            }
        }
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

    /// <summary>The motion, the creep and the heal. Call once a frame.</summary>
    public void Step(double dt, double level, double cap)
    {
        if (dt <= 0) return;
        _time += dt;
        Billow((H + 4) / 4);
        Coast(dt);
        Carry(dt);
        float k = (float)(1 - Math.Exp(-Math.Max(0, Heal) * dt)), top = (float)MaxPile, lv = (float)level, fcap = (float)cap;
        bool any = false;
        for (int gy = 0, i = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++, i++)
            {
                float f = Fog[i];
                if (f < 0) f = 0; else if (f > top) f = top;
                float t = (lv - _edge[i]) * 2.6f;
                t = t <= 0 ? 0 : t >= 1 ? fcap : t * fcap;
                float n = f + (t - f) * k;
                if (n > 0.003f) any = true;
                Fog[i] = n;
            }
        // Fog that is there is always drifting; an empty monitor has nothing to repaint.
        if (any) Dirty = true;
    }

    /// <summary>The fog's own velocity runs down and drags the texture.</summary>
    private void Coast(double dt)
    {
        if (!_active) return;
        float damp = (float)Math.Exp(-dt / MomentumTime), sdamp = (float)Math.Exp(-dt / ShiftTime), fdt = (float)dt * 0.5f;
        int bx1 = -1; bool shifted = false;
        for (int gy = 0, i = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++, i++)
            {
                float vx = Vx[i] * damp, vy = Vy[i] * damp;
                if (vx < 0.03f && vx > -0.03f && vy < 0.03f && vy > -0.03f) vx = vy = 0;
                else bx1 = gx;
                Vx[i] = vx; Vy[i] = vy;
                float sx = (ShiftX[i] + vx * fdt) * sdamp, sy = (ShiftY[i] + vy * fdt) * sdamp;
                if (sx < 0.01f && sx > -0.01f && sy < 0.01f && sy > -0.01f) sx = sy = 0; else shifted = true;
                ShiftX[i] = sx; ShiftY[i] = sy;
            }
        // The velocity stays where it was put and runs down there. (Letting it carry itself
        // too made a turning patch fling its fog outward into a ring.)
        if (bx1 < 0) _active = shifted;
    }

    /// <summary>The slope a cell is read with when it hands fog to a neighbour: the middle one,
    /// held to twice the gentler side, flat at a peak. Keeps thin arms and wisps from smearing
    /// as they travel without ever inventing a ripple.</summary>
    private static float MinMod(float a, float b)
    {
        if (a > 0)
        {
            if (b <= 0) return 0;
            float m = 0.5f * (a + b), l = 2 * (a < b ? a : b);
            return m < l ? m : l;
        }
        if (b >= 0) return 0;
        float n = 0.5f * (a + b), k = 2 * (a > b ? a : b);
        return n > k ? n : k;
    }

    /// <summary>
    /// Move the density with the flow (the fog's own velocity plus the billow). What leaves one
    /// cell arrives in its neighbour, so nothing is made or lost here: pushed fog piles up.
    /// Where the pool is winding this frame it carries the fog itself, so the velocity waits there.
    /// </summary>
    private void Carry(double dt)
    {
        int n1 = W + 1; float umax = 0, top = (float)MaxSpeed; float drift = (float)Math.Max(0, Drift);
        for (int gy = 0, i = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++, i++)
            {
                float b = _psi[(gy + 1) * n1 + gx + 1];
                float u = (b - _psi[gy * n1 + gx + 1]) * drift, v = (_psi[(gy + 1) * n1 + gx] - b) * drift;
                if (_active)
                {
                    if (gx < W - 1) u += 0.5f * (Vx[i] + Vx[i + 1]) * (1 - Math.Max(_hold[i], _hold[i + 1]));
                    if (gy < H - 1) v += 0.5f * (Vy[i] + Vy[i + W]) * (1 - Math.Max(_hold[i], _hold[i + W]));
                }
                // Billow and push together never beat the speed limit (it keeps the step to two passes).
                if (u > top) u = top; else if (u < -top) u = -top;
                if (v > top) v = top; else if (v < -top) v = -top;
                _ux[i] = u; _uy[i] = v;
                float au = u < 0 ? -u : u, av = v < 0 ? -v : v;
                if (au > umax) umax = au; if (av > umax) umax = av;
            }
        if (_held) { Array.Clear(_hold); _held = false; }
        int steps = Math.Clamp((int)Math.Ceiling(umax * dt / 0.45), 1, 8);
        float h = (float)(dt / steps);
        var F = Fog; var fl = _scratch;
        for (int s = 0; s < steps; s++)
        {
            // Across.
            for (int gy = 0; gy < H; gy++)
            {
                int row = gy * W;
                for (int gx = 0; gx < W - 1; gx++)
                {
                    int i = row + gx; float c = _ux[i] * h, q = 0;
                    if (c > 0.45f) c = 0.45f; else if (c < -0.45f) c = -0.45f;
                    if (c > 0)
                    {
                        float f0 = F[i];
                        if (f0 > 0) q = c * (f0 + 0.5f * (1 - c) * MinMod(f0 - (gx > 0 ? F[i - 1] : f0), F[i + 1] - f0));
                    }
                    else if (c < 0)
                    {
                        float f0 = F[i + 1];
                        if (f0 > 0) q = c * (f0 + 0.5f * (1 + c) * MinMod(f0 - (gx < W - 2 ? F[i + 2] : f0), F[i] - f0));
                    }
                    fl[i] = q;
                }
                for (int gx = 0; gx < W - 1; gx++) { int i = row + gx; float q = fl[i]; F[i] -= q; F[i + 1] += q; }
            }
            // Down.
            for (int gy = 0; gy < H - 1; gy++)
            {
                int row = gy * W;
                for (int gx = 0; gx < W; gx++)
                {
                    int i = row + gx; float c = _uy[i] * h, q = 0;
                    if (c > 0.45f) c = 0.45f; else if (c < -0.45f) c = -0.45f;
                    if (c > 0)
                    {
                        float f0 = F[i];
                        if (f0 > 0) q = c * (f0 + 0.5f * (1 - c) * MinMod(f0 - (gy > 0 ? F[i - W] : f0), F[i + W] - f0));
                    }
                    else if (c < 0)
                    {
                        float f0 = F[i + W];
                        if (f0 > 0) q = c * (f0 + 0.5f * (1 + c) * MinMod(f0 - (gy < H - 2 ? F[i + 2 * W] : f0), F[i] - f0));
                    }
                    fl[i] = q;
                }
            }
            for (int gy = 0; gy < H - 1; gy++)
                for (int gx = 0, i = gy * W; gx < W; gx++, i++) { float q = fl[i]; F[i] -= q; F[i + W] += q; }
        }
    }

    private void Limit(int i)
    {
        float vx = Vx[i], vy = Vy[i]; double m = Math.Sqrt(vx * vx + vy * vy);
        if (m > MaxSpeed) { float k = (float)(MaxSpeed / m); Vx[i] = vx * k; Vy[i] = vy * k; }
    }

    /// <summary>Start (or restart) with the fog already at its creep for this level.</summary>
    public void Settle(double level, double cap)
    {
        for (int gy = 0, i = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++, i++) Fog[i] = (float)Target(gx, gy, level, cap);
        Array.Clear(Mem);
        Array.Clear(Vx); Array.Clear(Vy); Array.Clear(ShiftX); Array.Clear(ShiftY); Array.Clear(_hold);
        _active = false; _held = false;
        Dirty = true;
    }

    /// <summary>A soft wipe that takes time (the cursor, the bouncing word). <paramref name="mark"/>
    /// = remember the spot so words come back in the fog there.</summary>
    /// <summary>
    /// The wipe is not a circle: its rim comes in and out by direction and keeps shifting, the way
    /// a hand through steam leaves a ragged edge. 0.6 to 1 of the radius.
    /// </summary>
    private double Rag(double dx, double dy)
    {
        double a = Math.Atan2(dy, dx);
        return 0.8 + 0.11 * Math.Sin(3 * a + _time * 0.9) + 0.06 * Math.Sin(5 * a - _time * 1.4 + 1.3)
                   + 0.03 * Math.Sin(9 * a + _time * 2.1 + 0.4);
    }

    public void Wipe(double xPx, double yPx, double radiusPx, double dt, bool mark)
    {
        double cx = Cx(xPx), cy = Cy(yPx), r = Math.Max(1, Cells(radiusPx));
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(W - 1, (int)(cx + r) + 1);
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(H - 1, (int)(cy + r) + 1);
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double d = Math.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
                double rr = r * Rag(gx - cx, gy - cy);
                if (d >= rr) continue;
                double w = 1 - d / rr; int i = gy * W + gx;
                Fog[i] *= (float)(1 - Math.Clamp(dt * WipeRate * w, 0, 1));
                if (mark) Mem[i] = Math.Max(Mem[i], (float)Math.Min(1, w * 4));
            }
        Dirty = true;
    }

    /// <summary>
    /// The cursor moved through the fog from one point to the next in <paramref name="dt"/>
    /// seconds. Unlike <see cref="Wipe"/> it does not rub the fog out: what it clears is shoved
    /// ahead and to the sides (more of it the faster the move), and the fog around is set moving
    /// the same way with a return flow at the flanks, so it curls in behind and drifts back.
    /// A cursor that did not move is a plain <see cref="Wipe"/>.
    /// </summary>
    public void Push(double x0Px, double y0Px, double x1Px, double y1Px, double radiusPx, double dt, bool mark)
    {
        if (dt <= 0) return;
        double dxp = x1Px - x0Px, dyp = y1Px - y0Px, dist = Math.Sqrt(dxp * dxp + dyp * dyp);
        // No move, or a jump too far to be a stroke (another monitor, a warp): only the spot.
        if (dist < 0.5 || dist > radiusPx * 40) { Wipe(x1Px, y1Px, radiusPx, dt, mark); return; }
        double r = Math.Max(1, Cells(radiusPx)), ux = dxp / dist, uy = dyp / dist, speed = dist / dt;
        double shove = Math.Clamp(speed / (Math.Max(1, radiusPx) * ShoveFullAt), 0, 1) * ShoveKeep;
        // Stamp along the stroke so a fast move leaves a path, not a row of dots.
        int n = Math.Clamp((int)Math.Ceiling(dist / (radiusPx * 0.5)), 1, 12);
        for (int k = 1; k <= n; k++)
            Stamp(Cx(x0Px + dxp * k / n), Cy(y0Px + dyp * k / n), r, ux, uy, dt / n, shove, mark);

        // Set the fog around moving: forward on the path, back along the flanks (a pair of eddies).
        double cx = Cx(x1Px), cy = Cy(y1Px), sg = Math.Max(3, r * 1.4), reach = sg * 3;
        double u = Math.Min(Cells(speed) * 0.5, MaxSpeed * 0.85), gain = 1 - Math.Exp(-dt * 10);
        int x0 = Math.Max(0, (int)(cx - reach)), x1 = Math.Min(W - 1, (int)(cx + reach) + 1);
        int y0 = Math.Max(0, (int)(cy - reach)), y1 = Math.Min(H - 1, (int)(cy + reach) + 1);
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double rx = gx - cx, ry = gy - cy, g = Math.Exp(-(rx * rx + ry * ry) / (2 * sg * sg));
                if (g < 0.012) continue;
                double cr = (ux * ry - uy * rx) / (sg * sg), e = Math.Sqrt(g) * gain;
                int i = gy * W + gx;
                Vx[i] += (float)((u * g * (ux - cr * ry) - Vx[i]) * e);
                Vy[i] += (float)((u * g * (uy + cr * rx) - Vy[i]) * e);
                Limit(i);
            }
        _active = true;
    }

    /// <summary>One dab of a push: clear under it, and lay what was cleared in a bank ahead and aside.</summary>
    private void Stamp(double cx, double cy, double r, double ux, double uy, double dt, double shove, bool mark)
    {
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(W - 1, (int)(cx + r) + 1);
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(H - 1, (int)(cy + r) + 1);
        double taken = 0;
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double d = Math.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
                double rr = r * Rag(gx - cx, gy - cy);
                if (d >= rr) continue;
                double w = 1 - d / rr; int i = gy * W + gx;
                float f = Fog[i], cut = f * (float)Math.Clamp(dt * WipeRate * w, 0, 1);
                Fog[i] = f - cut; taken += cut;
                if (mark) Mem[i] = Math.Max(Mem[i], (float)Math.Min(1, w * 4));
            }
        Dirty = true;
        taken *= shove;
        if (taken <= 1e-6) return;
        // The bank: a ring just outside the dab, most of it ahead, a good share at the sides,
        // little behind.
        double inner = r * 0.8, band = Math.Max(2, r * 2.4), outer = inner + band;
        double bx = cx + ux * r * 0.3, by = cy + uy * r * 0.3;
        x0 = Math.Max(0, (int)(bx - outer)); x1 = Math.Min(W - 1, (int)(bx + outer) + 1);
        y0 = Math.Max(0, (int)(by - outer)); y1 = Math.Min(H - 1, (int)(by + outer) + 1);
        double sum = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1 && sum <= 1e-9) return;
            for (int gy = y0; gy <= y1; gy++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    double rx = gx - bx, ry = gy - by, d = Math.Sqrt(rx * rx + ry * ry);
                    if (d <= inner || d >= outer) continue;
                    double w = Math.Sin(Math.PI * (d - inner) / band) * (0.12 + 0.88 * (0.5 + 0.5 * (rx * ux + ry * uy) / d));
                    if (pass == 0) sum += w;
                    else { int i = gy * W + gx; Fog[i] = (float)Math.Min(MaxPile, Fog[i] + taken * w / sum); }
                }
        }
    }

    /// <summary>An instant soft hole (a pop, a break, a gold corner). Fades at its rim. The fog it
    /// clears is thrown outward as a puff: a bank around the hole that keeps travelling a moment.</summary>
    public void Hole(double xPx, double yPx, double radiusPx)
    {
        double cx = Cx(xPx), cy = Cy(yPx), r = Math.Max(1.5, Cells(radiusPx)), reach = r * 2.4;
        int x0 = Math.Max(0, (int)(cx - reach)), x1 = Math.Min(W - 1, (int)(cx + reach) + 1);
        int y0 = Math.Max(0, (int)(cy - reach)), y1 = Math.Min(H - 1, (int)(cy + reach) + 1);
        double taken = 0, sum = 0, band = r * 1.4;
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double rx = gx - cx, ry = gy - cy, d = Math.Sqrt(rx * rx + ry * ry);
                if (d >= reach) continue;
                int i = gy * W + gx;
                if (d < r)
                {
                    float f = Fog[i], cut = f * (float)Math.Clamp((1 - d / r) * 2.2, 0, 1);
                    Fog[i] = f - cut; taken += cut;
                }
                else if (d < r + band) sum += Math.Sin(Math.PI * (d - r) / band);
                if (d > 0.01)
                {
                    // A ring of outward speed at the rim: the middle is left alone so the hole heals as ever.
                    double q = (d - r) / (r * 0.45), p = Math.Exp(-q * q) * PuffSpeed / d;
                    Vx[i] += (float)(rx * p); Vy[i] += (float)(ry * p);
                    Limit(i);
                }
            }
        taken *= 0.4;
        if (taken > 1e-6 && sum > 1e-9)
            for (int gy = y0; gy <= y1; gy++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    double d = Math.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
                    if (d < r || d >= r + band) continue;
                    int i = gy * W + gx;
                    Fog[i] = (float)Math.Min(MaxPile, Fog[i] + taken * Math.Sin(Math.PI * (d - r) / band) / sum);
                }
        _active = true;
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
    /// The fog under the pool is also left turning (<see cref="PoolMomentum"/>), so when the pool
    /// moves on or shrinks the patch keeps winding and coasts to a stop.
    /// </summary>
    public void Swirl(double xPx, double yPx, double radiusPx, double eased, double angle, double spin, double turns, double dt, double cap, int arms = 3)
    {
        if (dt <= 0 || eased <= 0.01 || radiusPx <= 1) return;
        double cx = Cx(xPx), cy = Cy(yPx), r = Cells(radiusPx);
        // The grid is not square in pixels when the monitor is not 160:H; work in cell units, the
        // error is a slightly oval swirl and nobody can see it.
        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(W - 1, (int)(cx + r) + 1);
        int y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(H - 1, (int)(cy + r) + 1);
        if (x0 > x1 || y0 > y1) return;
        Array.Copy(Fog, _scratch, Fog.Length);
        double eye = r * EyeShare, pitch = r / (Math.Max(0.2, turns) * 2 * Math.PI);
        // What the pool leaves behind: the whole patch turning as one body (so the arms keep
        // their shape while they coast), no faster at the rim than PoolMomentum.
        double vgain = 1 - Math.Exp(-dt * 6);
        double turn = Math.Sign(spin) * Math.Min(Math.Abs(spin), PoolMomentum / r) * Math.Min(1, eased);
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                double dx = gx - cx, dy = gy - cy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= r) continue;
                int i = gy * W + gx;
                // The reach is not a ruled circle: its rim wanders by direction and with time.
                double phi = Math.Atan2(dy, dx);
                double rim = 0.9 + 0.07 * Math.Sin(3 * phi + angle * 0.4) + 0.03 * Math.Sin(7 * phi - angle * 0.6 + 1.1);
                double edge = 1 - d / (r * rim);              // 0 at the rim, 1 at the centre
                if (edge <= 0) continue;
                double soft = Math.Min(1, edge * 2.5);        // the reach fades in over its outer 40%
                soft = soft * soft * (3 - 2 * soft);          // with no corner at either end
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
                double arm = 0.5 - 0.5 * Math.Cos(arms * (phi - Whirlpool.Wobble(d / r, phi, angle)) - arms * d / Math.Max(0.5, pitch) + arms * angle);
                f += (arm - 0.5) * band * soft * 3.2 * dt * cap;
                if (d < eye) f *= Math.Clamp(1 - (1 - d / eye) * 6 * dt * (0.4 + eased), 0, 1);
                // Fog a push piled up above 1 is left as it is, or the rim would cut a line in it.
                Fog[i] = (float)Math.Clamp(f, 0, Math.Max(1, Fog[i]));
                // Momentum: the same turn as the carry above, as a velocity the fog keeps. While
                // the pool is here it does the carrying itself, so the velocity is held back.
                float gk = (float)(vgain * soft);
                Vx[i] += ((float)(turn * dy) - Vx[i]) * gk;
                Vy[i] += ((float)(-turn * dx) - Vy[i]) * gk;
                if (soft > _hold[i]) _hold[i] = (float)soft;
            }
        _active = true; _held = true;
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
