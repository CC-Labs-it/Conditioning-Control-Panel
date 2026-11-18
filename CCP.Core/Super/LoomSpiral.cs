namespace ConditioningControlPanel.SuperCore;

/// <summary>One weave of a Loom spiral (the Loom's schema v2 "layer").</summary>
public sealed class LoomLayer
{
    public int Arms = 4;
    public double Turns = 2, Duty = 0.5;
    /// <summary>0 log, 1 arch, 2 ribbon, 3 golden, 4 tunnel, 5 petal (the Loom's LOOM_STYLES order).</summary>
    public int Style;
    public int Direction = 1;
    /// <summary>1 to 6 threads, 0xRRGGBB.</summary>
    public uint[] Colors = { 0xff69b4 };
    public bool Gradient;
    public int SpeedMul = 1;
}

/// <summary>
/// A Loom spiral as numbers: what the Loom's dials hold (Resources/web/dtrh/shared/loomField.js,
/// schema v2), less the background, the hue drift and the centrepiece, which the Whirlpool does
/// not wear. No WPF, no Skia.
/// </summary>
public sealed class LoomSpec
{
    public LoomLayer Layer = new(), Layer2 = new();
    public bool Layer2On;
    public double Glow, PulseAmp, PulseCycles = 1, WobAmp, WobFreq = 2, WobCycles = 1;

    /// <summary>The Loom's own swatches.</summary>
    public static readonly uint[] Swatches = { 0xff69b4, 0xe56cc0, 0x8a5cff, 0x00e5ff, 0x39ff9d, 0xffd94d, 0xff6a2f, 0xffffff };

    private static double Snap(double v, double step) => Math.Round(v / step) * step;

    private static uint[] Threads(int count, Random rng)
    {
        var pool = (uint[])Swatches.Clone();
        for (int i = pool.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        return pool[..Math.Clamp(count, 1, pool.Length)];
    }

    /// <summary>A random but tasteful spiral: the Loom's dice button (randomParams2), same odds.</summary>
    public static LoomSpec Random(Random rng)
    {
        var d = new LoomSpec();
        d.Layer.Arms = 1 + rng.Next(8);
        d.Layer.Turns = Math.Clamp(Snap(0.5 + rng.NextDouble() * 3.5, 0.25), 0.5, 6);
        d.Layer.Duty = Math.Clamp(Snap(0.3 + rng.NextDouble() * 0.4, 0.05), 0.2, 0.8);
        d.Layer.Style = rng.Next(6);
        d.Layer.Direction = rng.NextDouble() < 0.5 ? 1 : -1;
        d.Layer.Colors = Threads(1 + rng.Next(3), rng);
        d.Layer.Gradient = rng.NextDouble() < 0.35;
        d.Layer2On = rng.NextDouble() < 0.35;
        d.Layer2.Arms = 2 + rng.Next(5);
        d.Layer2.Turns = Math.Clamp(Snap(0.5 + rng.NextDouble() * 2.5, 0.25), 0.5, 6);
        d.Layer2.Colors = Threads(1, rng);
        d.Layer2.Direction = -d.Layer.Direction;
        d.Glow = rng.NextDouble() < 0.4 ? 0.3 + rng.NextDouble() * 0.5 : 0;
        if (rng.NextDouble() < 0.35) { d.PulseAmp = 0.05 + rng.NextDouble() * 0.12; d.PulseCycles = 1 + rng.Next(2); }
        if (rng.NextDouble() < 0.3) { d.WobAmp = 0.08 + rng.NextDouble() * 0.15; d.WobFreq = 2 + rng.Next(3); d.WobCycles = 1; }
        return d;
    }
}

/// <summary>
/// The Whirlpool's spiral over time: a Loom spiral that holds for a while and then melts into a
/// newly rolled one, or one pinned spiral that stays. Step it, then read <see cref="From"/>,
/// <see cref="To"/> and <see cref="Mix"/>.
/// </summary>
public sealed class LoomMorph
{
    /// <summary>How long a spiral holds before the next one, and how long the melt takes (s).</summary>
    public const double HoldSec = 18, MorphSec = 5, PinSec = 1.6;

    private readonly Random _rng;
    private double _age, _span = MorphSec;
    private bool _morphing;
    private LoomSpec? _pinned;

    /// <summary>Seconds a spiral holds before the next one rolls in, set from the player's
    /// "New spiral every" (<see cref="WhirlpoolOptions.ClampHold"/>). Unused while one is pinned.</summary>
    public double Hold { get; set; } = HoldSec;

    public LoomSpec From { get; private set; }
    public LoomSpec? To { get; private set; }
    /// <summary>0 = all <see cref="From"/>, 1 = all <see cref="To"/> (eased).</summary>
    public double Mix { get; private set; }

    public LoomMorph(Random rng) { _rng = rng; From = LoomSpec.Random(rng); }

    /// <summary>Pin one spiral (it melts in and stays), or null to go back to always new ones.</summary>
    public void Pin(LoomSpec? spec)
    {
        if (ReferenceEquals(spec, _pinned)) return;
        _pinned = spec;
        if (spec != null) Begin(spec, PinSec);
        else _age = double.PositiveInfinity;   // roll a new one on the next step
    }

    private void Begin(LoomSpec to, double span)
    {
        // A melt already under way is cut short at whichever end it is nearer to.
        if (_morphing && To != null && Mix >= 0.5) From = To;
        To = to; _morphing = true; _age = 0; _span = span; Mix = 0;
    }

    public void Step(double dt)
    {
        if (dt <= 0) return;
        _age += dt;
        if (_morphing)
        {
            double k = Math.Clamp(_age / _span, 0, 1);
            Mix = k * k * (3 - 2 * k);
            if (k >= 1) { From = To!; To = null; Mix = 0; _morphing = false; _age = 0; }
        }
        else if (_pinned == null && _age >= Hold) Begin(LoomSpec.Random(_rng), MorphSec);
    }
}

/// <summary>
/// The Loom's field shader on the CPU: one spiral into a small square of premultiplied RGBA,
/// see-through where the Loom would show its background. The maths is the shader's, line for
/// line (layerGlsl in loomField.js); r = 1 is the edge of the square's inscribed circle.
/// </summary>
public sealed class LoomSpiral
{
    private const double Tau = Math.PI * 2;
    public readonly int Size;
    private readonly float[] _r, _th, _acc;
    private readonly float[] _t1 = new float[513], _t2 = new float[513];

    public LoomSpiral(int size)
    {
        Size = Math.Max(16, size);
        int n = Size * Size;
        _r = new float[n]; _th = new float[n]; _acc = new float[n * 4];
        double c = Size / 2.0;
        for (int y = 0, i = 0; y < Size; y++)
            for (int x = 0; x < Size; x++, i++)
            {
                double px = (x + 0.5 - c) / c, py = (y + 0.5 - c) / c;
                _r[i] = (float)Math.Sqrt(px * px + py * py);
                _th[i] = (float)Math.Atan2(py, px);
            }
    }

    public void Clear() => Array.Clear(_acc);

    private static double Frac(double v) => v - Math.Floor(v);
    private static double Smooth(double a, double b, double x)
    {
        if (b <= a) return x < a ? 0 : 1;
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static void StyleTable(float[] t, int style)
    {
        for (int k = 0; k <= 512; k++)
        {
            double rc = k / 512.0;
            t[k] = (float)(style switch
            {
                0 => Math.Pow(rc, 0.5714286),
                1 => rc,
                2 => 0.5 - Math.Sin(Math.Asin(1 - 2 * rc) / 3),
                _ => Math.Log(1 + rc * 6) / 1.9459101,
            });
        }
    }

    private static void Pick(LoomLayer l, double idx, out float r, out float g, out float b)
    {
        int n = l.Colors.Length, i = ((int)Math.Floor(idx) % n + n) % n;
        uint c = l.Colors[i];
        r = ((c >> 16) & 255) / 255f; g = ((c >> 8) & 255) / 255f; b = (c & 255) / 255f;
    }

    private static void Blend(LoomLayer l, double gpos, out float r, out float g, out float b)
    {
        double i0 = Math.Floor(gpos); float f = (float)(gpos - i0);
        Pick(l, i0, out float r0, out float g0, out float b0);
        Pick(l, i0 + 1, out float r1, out float g1, out float b1);
        r = r0 + (r1 - r0) * f; g = g0 + (g1 - g0) * f; b = b0 + (b1 - b0) * f;
    }

    private static double LayerAt(LoomLayer l, float[] table, double r, double th, double wob, double rot, double glow, double px,
        out float cr, out float cg, out float cb)
    {
        double rc = Math.Clamp(r, 0, 1), t = table[(int)(rc * 512)];
        int arms = Math.Max(1, l.Arms), count = Math.Max(1, l.Colors.Length);
        double duty = l.Duty, cov;
        if (l.Style < 4)
        {
            double a = th - rot - Tau * l.Turns * t + wob, qf = Frac(a / Tau), pos = qf * arms, armIdx = Math.Floor(pos), s = pos - armIdx;
            double aa = Math.Min(px * arms / (Tau * Math.Max(rc, 0.02)), 0.45);
            cov = Smooth(0, aa, s) * (1 - Smooth(duty, duty + aa, s));
            if (glow > 0.001)
            {
                double k = 14 + (4 - 14) * glow, halo = Math.Exp(-Math.Max(0, s - duty) * k) + Math.Exp(-(1 - s) * k);
                cov = Math.Min(1, cov + glow * 0.55 * halo * (1 - cov));
            }
            if (!l.Gradient) Pick(l, armIdx, out cr, out cg, out cb);
            else Blend(l, Frac(qf * arms / count) * count, out cr, out cg, out cb);
        }
        else if (l.Style == 4)
        {
            double shift = rot * arms / Tau, helix = Math.Floor((l.Turns - 0.5) / count + 0.5) * count;
            double pos = t * arms - shift + helix * th / Tau + wob * 0.5, s = Frac(pos);
            double aa = Math.Min(px * arms * 1.5, 0.45);
            cov = Smooth(0, aa, s) * (1 - Smooth(duty, duty + aa, s));
            if (glow > 0.001)
            {
                double k = 14 + (4 - 14) * glow, halo = Math.Exp(-Math.Max(0, s - duty) * k) + Math.Exp(-(1 - s) * k);
                cov = Math.Min(1, cov + glow * 0.55 * halo * (1 - cov));
            }
            if (!l.Gradient) Pick(l, Math.Floor(pos), out cr, out cg, out cb);
            else Blend(l, Frac(pos / count) * count, out cr, out cg, out cb);
        }
        else
        {
            double a = th - rot - Tau * l.Turns * t * 0.35 + wob, m = 0.5 + 0.5 * Math.Cos(a * arms);
            double aa = Math.Min(px * arms * 2, 0.45), thr = 1 - duty;
            cov = Smooth(thr - aa, thr + aa, m);
            cov *= Smooth(0.03, 0.18, rc) * (1 - Smooth(0.85, 1, rc));
            if (glow > 0.001)
            {
                double halo = Smooth(thr - 0.35 * glow - aa, thr, m);
                cov = Math.Min(1, cov + glow * 0.5 * halo * (1 - cov));
            }
            if (!l.Gradient) Pick(l, Math.Floor(Frac(a / Tau) * arms + 0.5), out cr, out cg, out cb);
            else Blend(l, Frac(a / Tau * arms / count) * count, out cr, out cg, out cb);
        }
        return cov;
    }

    /// <summary>
    /// Add one spiral to the picture at <paramref name="weight"/> (two calls whose weights sum to 1
    /// are a melt from one into the other). <paramref name="turn"/> is how far it has turned, in
    /// radians; <paramref name="phase"/> runs the pulse and the wobble (one unit = one cycle).
    /// </summary>
    public void Add(LoomSpec q, double turn, double phase, double weight)
    {
        if (weight <= 0.002) return;
        StyleTable(_t1, q.Layer.Style);
        if (q.Layer2On) StyleTable(_t2, q.Layer2.Style);
        double px = 2.0 / Size, zoom = 1 + q.PulseAmp * Math.Sin(Tau * phase * q.PulseCycles);
        double rot1 = q.Layer.Direction * turn * q.Layer.SpeedMul, rot2 = q.Layer2.Direction * turn * q.Layer2.SpeedMul;
        float w = (float)weight;
        for (int i = 0, j = 0; i < _r.Length; i++, j += 4)
        {
            double r = _r[i] / zoom;
            if (r >= 1) continue;
            double th = _th[i];
            double wob = q.WobAmp > 0.001 ? q.WobAmp * Math.Sin(Tau * (q.WobFreq * r + phase * q.WobCycles)) : 0;
            double a = LayerAt(q.Layer, _t1, r, th, wob, rot1, q.Glow, px, out float cr, out float cg, out float cb);
            float pr = (float)(cr * a), pg = (float)(cg * a), pb = (float)(cb * a), pa = (float)a;
            if (q.Layer2On)
            {
                float a2 = (float)(LayerAt(q.Layer2, _t2, r, th, wob, rot2, q.Glow, px, out float r2, out float g2, out float b2) * 0.85);
                pr = pr * (1 - a2) + r2 * a2; pg = pg * (1 - a2) + g2 * a2; pb = pb * (1 - a2) + b2 * a2; pa = pa * (1 - a2) + a2;
            }
            _acc[j] += pr * w; _acc[j + 1] += pg * w; _acc[j + 2] += pb * w; _acc[j + 3] += pa * w;
        }
    }

    /// <summary>Write the picture as premultiplied RGBA bytes (Size x Size x 4).</summary>
    public void CopyTo(byte[] rgba)
    {
        int n = Math.Min(rgba.Length, _acc.Length);
        for (int i = 0; i < n; i++) rgba[i] = (byte)(Math.Clamp(_acc[i], 0f, 1f) * 255f + 0.5f);
    }

    /// <summary>Write the picture as premultiplied BGRA bytes (the compositor's cheap stretch format), laid over
    /// an opaque <paramref name="groundRgb"/> so the result has no see-through pixels.</summary>
    public void CopyToBgraOver(byte[] bgra, uint groundRgb)
    {
        float gr = ((groundRgb >> 16) & 255) / 255f, gg = ((groundRgb >> 8) & 255) / 255f, gb = (groundRgb & 255) / 255f;
        int n = Math.Min(bgra.Length, _acc.Length);
        for (int i = 0; i + 3 < n; i += 4)
        {
            float a = Math.Clamp(_acc[i + 3], 0f, 1f), k = 1 - a;
            bgra[i] = (byte)(Math.Clamp(_acc[i + 2] + gb * k, 0f, 1f) * 255f + 0.5f);
            bgra[i + 1] = (byte)(Math.Clamp(_acc[i + 1] + gg * k, 0f, 1f) * 255f + 0.5f);
            bgra[i + 2] = (byte)(Math.Clamp(_acc[i] + gr * k, 0f, 1f) * 255f + 0.5f);
            bgra[i + 3] = 255;
        }
    }

    /// <summary>Coverage 0..1 at a pixel of the picture (tests).</summary>
    public double AlphaAt(int x, int y) => _acc[(y * Size + x) * 4 + 3];
}
