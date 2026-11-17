namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Two-tone fog (owner, 2026-11-17: "mix 2 colors, provided we can have a cool visual eff on the fog"):
/// the second colour moves through the first like ink in water. A few soft blobs drift on their own
/// slow paths over a plane that slowly bends, so they stretch into bands that fold; where they meet the
/// colour walks the hue wheel the short way (pink to mint passes violet), so the seam glows instead of
/// going grey. Pure maths in screen units (height = 1); the pink filter and Steam draw it.
/// </summary>
public static class FogMix
{
    /// <summary>Entries in a colour ramp: index 0 is the first colour, the last is the second.</summary>
    public const int RampSize = 64;

    /// <summary>Blobs of the second colour. The mockup's fitting room uses five.</summary>
    public const int Blobs = 5;

    /// <summary>Where the blob field turns from the first colour to the second: a soft but definite seam.</summary>
    public const double SeamLow = 0.18, SeamHigh = 0.95;

    /// <summary>At full motion a blob crosses about a tenth of the screen height a second, so frames a few
    /// seconds apart differ plainly while the fog never looks hurried. The raw blob field at (<paramref name="x"/>, <paramref name="y"/>), screen units with the
    /// height = 1 and x in 0..<paramref name="aspect"/>, at mix time <paramref name="t"/> seconds.</summary>
    public static double Field(double x, double y, double aspect, double t)
    {
        // The fold: the plane bends on two slow waves each way, so round blobs stretch and curl.
        double wx = x + 0.11 * Math.Sin(y * 2.6 + t * 0.14) + 0.05 * Math.Sin(y * 5.3 - t * 0.19 + 1.7);
        double wy = y + 0.10 * Math.Sin(x * 2.2 - t * 0.12 + 0.6) + 0.05 * Math.Sin(x * 4.9 + t * 0.16 + 2.9);
        double s = 0;
        for (int i = 0; i < Blobs; i++)
        {
            double bx = aspect * (0.5 + 0.42 * Math.Sin(t * (0.10 + i * 0.022) + i * 2.1));
            double by = 0.5 + 0.40 * Math.Cos(t * (0.08 + i * 0.028) + i * 1.3);
            double r = 0.27 + 0.07 * Math.Sin(t * 0.13 + i * 1.7);
            double dx = wx - bx, dy = wy - by;
            s += Math.Exp(-(dx * dx + dy * dy) / (r * r));
        }
        return s;
    }

    /// <summary>How much of the second colour a field value shows, 0..1, smooth at the seam.</summary>
    public static double Shape(double s)
    {
        double k = Math.Clamp((s - SeamLow) / (SeamHigh - SeamLow), 0, 1);
        return k * k * (3 - 2 * k);
    }

    /// <summary>The ramp from <paramref name="a"/> to <paramref name="b"/>: hue the short way round, saturation
    /// and value straight across, then lifted toward white wherever the walk is darker than the two ends
    /// (pink to mint passes blue, which would read as a dark outline). Ends are exact. A grey end borrows
    /// the other end's hue.</summary>
    public static (byte R, byte G, byte B)[] Ramp((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var ramp = new (byte R, byte G, byte B)[RampSize];
        var (ha, sa, va) = ToHsv(a);
        var (hb, sb, vb) = ToHsv(b);
        if (sa < 0.05) ha = hb;
        if (sb < 0.05) hb = ha;
        double dh = hb - ha;
        if (dh > 180) dh -= 360;
        if (dh < -180) dh += 360;
        for (int i = 0; i < RampSize; i++)
        {
            double k = i / (double)(RampSize - 1);
            var c = FromHsv(ha + dh * k, sa + (sb - sa) * k, va + (vb - va) * k);
            double want = Luma(a) + (Luma(b) - Luma(a)) * k, have = Luma(c);
            if (have < want && have < 1)
            {
                double m = (want - have) / (1 - have);
                c = ((byte)Math.Round(c.R + (255 - c.R) * m), (byte)Math.Round(c.G + (255 - c.G) * m), (byte)Math.Round(c.B + (255 - c.B) * m));
            }
            ramp[i] = c;
        }
        ramp[0] = a;
        ramp[RampSize - 1] = b;
        return ramp;
    }

    /// <summary>Rough brightness 0..1 as the eye sees it (Rec. 709 weights on the stored values).</summary>
    public static double Luma((byte R, byte G, byte B) c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public static (double H, double S, double V) ToHsv((byte R, byte G, byte B) c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    public static (byte R, byte G, byte B) FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1); v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

/// <summary>
/// The blob field on a small grid for one screen and one moment, read back smoothly anywhere on it.
/// The pink filter paints the grid itself; Steam reads it at each fog cell, through the fog's own drag,
/// so the colours ride with the fog when the cursor or the Whirlpool moves it. Reused frame to frame.
/// </summary>
public sealed class FogMixField
{
    public int W { get; private set; }
    public int H { get; private set; }
    public float[] S { get; private set; } = Array.Empty<float>();

    /// <summary>Fill a <paramref name="w"/> by <paramref name="h"/> grid over a screen of the given
    /// <paramref name="aspect"/> (width over height) at mix time <paramref name="t"/>.</summary>
    public void Fill(int w, int h, double aspect, double t)
    {
        w = Math.Max(2, w); h = Math.Max(2, h);
        if (S.Length != w * h) S = new float[w * h];
        W = w; H = h;
        for (int gy = 0, i = 0; gy < h; gy++)
        {
            double y = (gy + 0.5) / h;
            for (int gx = 0; gx < w; gx++, i++)
                S[i] = (float)FogMix.Field((gx + 0.5) / w * aspect, y, aspect, t);
        }
    }

    /// <summary>The field at (<paramref name="fx"/>, <paramref name="fy"/>), each 0..1 across the screen; bilinear, clamped at the edges.</summary>
    public double At(double fx, double fy)
    {
        double x = Math.Clamp(fx * W - 0.5, 0, W - 1), y = Math.Clamp(fy * H - 0.5, 0, H - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, W - 1), y1 = Math.Min(y0 + 1, H - 1);
        double tx = x - x0, ty = y - y0;
        double top = S[y0 * W + x0] + (S[y0 * W + x1] - S[y0 * W + x0]) * tx;
        double bot = S[y1 * W + x0] + (S[y1 * W + x1] - S[y1 * W + x0]) * tx;
        return top + (bot - top) * ty;
    }
}
