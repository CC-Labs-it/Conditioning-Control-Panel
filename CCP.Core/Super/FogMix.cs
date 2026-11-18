namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Two-tone fog (owner, 2026-11-17: "mix 2 colors, provided we can have a cool visual eff on the fog"):
/// the second colour moves through the first like ink in water. A few soft blobs drift on their own
/// slow paths over a plane that slowly bends, so they stretch into bands that fold; where they meet the
/// colour walks the hue wheel the short way in OKLCH (pink to mint passes violet), so the seam glows instead
/// of going grey or brown, and each colour keeps a clean region of its own. Pure maths in screen units (height = 1); the pink filter and Steam draw it.
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

    /// <summary>Where along the ramp the walk between the two colours happens: each end holds its own colour
    /// for this share, so the fog reads as two clean colours with a seam, not one averaged tint. Kept moderate:
    /// the drawn grid is stretched, and too sharp a seam would let the stretch average the two in RGB again.</summary>
    public const double WalkLow = 0.16, WalkHigh = 0.84;

    /// <summary>The most the middle of a long hue walk gives up of its chroma (opposite colours), 0..1.</summary>
    public const double MidSoften = 0.28;

    /// <summary>The ramp from <paramref name="a"/> to <paramref name="b"/> in OKLCH (a perceptual space, Ottosson's
    /// OKLab in polar form): lightness and chroma straight across, hue the short way round, so the seam keeps its
    /// colour and lightness instead of going brown or grey the way an RGB or HSV average does (teal and amber met
    /// as a murky brown and dark green, 2026-11-17). Each end holds its colour for a stretch (<see cref="WalkLow"/>,
    /// <see cref="WalkHigh"/>); a colour past the screen's gamut loses chroma, never hue or lightness. Ends are
    /// exact. A grey end borrows the other end's hue. The page preview (prizecounter sheet/fogmix.js) runs the
    /// same maths.</summary>
    public static (byte R, byte G, byte B)[] Ramp((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var ramp = new (byte R, byte G, byte B)[RampSize];
        var (la, ca, ha) = ToOklch(a);
        var (lb, cb, hb) = ToOklch(b);
        if (ca < 0.02) ha = hb;
        if (cb < 0.02) hb = ha;
        double dh = hb - ha;
        if (dh > 180) dh -= 360;
        if (dh < -180) dh += 360;
        // Two colours far apart on the wheel pass a third on the way (pink to mint, sky to amber): the middle of
        // that walk is softened a little so the seam stays a seam and never reads as a third colour of its own.
        double far = Math.Clamp((Math.Abs(dh) - 90) / 90, 0, 1);
        for (int i = 0; i < RampSize; i++)
        {
            double u = Walk(i / (double)(RampSize - 1));
            double soften = 1 - MidSoften * far * Math.Sin(Math.PI * u);
            ramp[i] = FromOklch(la + (lb - la) * u, (ca + (cb - ca) * u) * soften, ha + dh * u);
        }
        ramp[0] = a;
        ramp[RampSize - 1] = b;
        return ramp;
    }

    /// <summary>How far along the walk a ramp position (0..1) is: 0 before <see cref="WalkLow"/>, 1 after
    /// <see cref="WalkHigh"/>, smooth between.</summary>
    public static double Walk(double k)
    {
        double x = Math.Clamp((k - WalkLow) / (WalkHigh - WalkLow), 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>sRGB to OKLCH: lightness 0..1, chroma (about 0..0.37), hue in degrees.</summary>
    public static (double L, double C, double H) ToOklch((byte R, byte G, byte B) c)
    {
        var (l, a, b) = ToOklab(c);
        double h = Math.Atan2(b, a) * 180 / Math.PI;
        if (h < 0) h += 360;
        return (l, Math.Sqrt(a * a + b * b), h);
    }

    /// <summary>OKLCH to sRGB. A colour the screen cannot show keeps its lightness and hue and loses chroma
    /// until it fits.</summary>
    public static (byte R, byte G, byte B) FromOklch(double l, double c, double h)
    {
        double rad = h * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        l = Math.Clamp(l, 0, 1);
        c = Math.Max(0, c);
        var rgb = OklabToLinear(l, c * cos, c * sin);
        if (!InGamut(rgb))
        {
            double lo = 0, hi = c;
            for (int i = 0; i < 18; i++)
            {
                double mid = (lo + hi) / 2;
                if (InGamut(OklabToLinear(l, mid * cos, mid * sin))) lo = mid; else hi = mid;
            }
            rgb = OklabToLinear(l, lo * cos, lo * sin);
        }
        return (Encode(rgb.R), Encode(rgb.G), Encode(rgb.B));
    }

    public static (double L, double A, double B) ToOklab((byte R, byte G, byte B) c)
    {
        double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static (double R, double G, double B) OklabToLinear(double l, double a, double b)
    {
        double l_ = l + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = l - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = l - 0.0894841775 * a - 1.2914855480 * b;
        double ll = l_ * l_ * l_, mm = m_ * m_ * m_, ss = s_ * s_ * s_;
        return (4.0767416621 * ll - 3.3077115913 * mm + 0.2309699292 * ss,
            -1.2684380046 * ll + 2.6097574011 * mm - 0.3413193965 * ss,
            -0.0041960863 * ll - 0.7034186147 * mm + 1.7076147010 * ss);
    }

    private static bool InGamut((double R, double G, double B) c)
    {
        const double e = 1e-4;
        return c.R >= -e && c.R <= 1 + e && c.G >= -e && c.G <= 1 + e && c.B >= -e && c.B <= 1 + e;
    }

    private static double Linear(byte v)
    {
        double c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Encode(double c)
    {
        c = Math.Clamp(c, 0, 1);
        double s = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(s, 0, 1) * 255);
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
