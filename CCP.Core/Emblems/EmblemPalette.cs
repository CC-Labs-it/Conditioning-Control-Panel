namespace ConditioningControlPanel.Emblems;

/// <summary>
/// Colour maths for Readable Rows, pure functions on ARGB (0xAARRGGBB). Every mix goes through
/// <see cref="Mix"/>, which mixes in OKLab like the mockup's CSS <c>color-mix(in oklab, ...)</c>.
/// </summary>
public static class EmblemPalette
{
    public const uint White = 0xFFFFFFFF;
    public const uint Pink = 0xFFFF69B4;       // PinkBrush, the fallback section hue
    public const uint TextLight = 0xFFF0F0F5;  // TextLightBrush
    public const uint Tape = 0xFFFFD35C;       // the mockup's --amber

    /// <summary>Sticker look: the white edge (CSS 1.1 px at a 34 px emblem), the dark offset below and the tilt.</summary>
    public const uint StickerEdge = White;
    public const double StickerEdgeDip = 1.1;
    public const uint StickerShadow = 0xFF000000;
    public const double StickerShadowOpacity = 0.45;
    public const double StickerShadowDyDip = 2.0;
    public const double StickerTiltDegrees = -4.0;

    /// <summary>An option that is OFF: label at 60%, emblem grey at 45%.</summary>
    public const double OffLabelOpacity = 0.6;
    public const double OffEmblemOpacity = 0.45;

    /// <summary>Share of white in Soft (Soft = 42% hue + 58% white).</summary>
    public const double SoftWhite = 0.58;
    /// <summary>Share of the hue in a row label (70% text light + 30% hue).</summary>
    public const double LabelHue = 0.30;
    /// <summary>Share of white in a row number (82% hue + 18% white).</summary>
    public const double ValueWhite = 0.18;

    /// <summary>Sticker ink for <paramref name="ink"/> under section <paramref name="hue"/>.</summary>
    public static uint Ink(EmblemInk ink, uint hue, uint? fixedArgb = null) => ink switch
    {
        EmblemInk.Main => hue,
        EmblemInk.Cut => White,
        EmblemInk.Soft => Mix(hue, White, SoftWhite),
        EmblemInk.Paper => White,
        EmblemInk.Pic => hue,
        EmblemInk.Tape => Tape,
        EmblemInk.Fixed => fixedArgb ?? hue,
        _ => hue,
    };

    /// <summary>The ink an OFF row draws: <see cref="Ink"/> turned grey (the whole emblem then sits at <see cref="OffEmblemOpacity"/>).</summary>
    public static uint OffInk(EmblemInk ink, uint hue, uint? fixedArgb = null) => Grey(Ink(ink, hue, fixedArgb));

    /// <summary>Row label colour: 70% <paramref name="textLight"/> + 30% <paramref name="hue"/>.</summary>
    public static uint LabelTint(uint textLight, uint hue) => Mix(textLight, hue, LabelHue);

    /// <summary>Row number colour: 82% hue + 18% white.</summary>
    public static uint ValueTint(uint hue) => Mix(hue, White, ValueWhite);

    /// <summary>
    /// CSS <c>grayscale(1)</c>: the Rec. 709 luma of the sRGB values, alpha kept.
    /// </summary>
    public static uint Grey(uint argb)
    {
        double r = R(argb), g = G(argb), b = B(argb);
        double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        byte v = ToByte(y);
        return Pack(A(argb), v, v, v);
    }

    /// <summary>
    /// <paramref name="a"/> mixed toward <paramref name="b"/> by <paramref name="t"/> (0 = a, 1 = b)
    /// in OKLab; alpha mixes linearly. The one mixing function of the feature.
    /// </summary>
    public static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        if (t == 0) return a;
        if (t == 1) return b;
        var (l1, a1, b1) = ToOklab(a);
        var (l2, a2, b2) = ToOklab(b);
        var rgb = FromOklab(l1 + (l2 - l1) * t, a1 + (a2 - a1) * t, b1 + (b2 - b1) * t);
        double alpha = A(a) + (A(b) - A(a)) * t;
        return Pack(ToByte(alpha), ToByte(rgb.R), ToByte(rgb.G), ToByte(rgb.B));
    }

    // ---------- OKLab (Björn Ottosson, 2020) ----------

    private static (double L, double A, double B) ToOklab(uint argb)
    {
        double r = Lin(R(argb) / 255.0), g = Lin(G(argb) / 255.0), b = Lin(B(argb) / 255.0);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static (double R, double G, double B) FromOklab(double L, double a, double b)
    {
        double l = L + 0.3963377774 * a + 0.2158037573 * b;
        double m = L - 0.1055613458 * a - 0.0638541728 * b;
        double s = L - 0.0894841775 * a - 1.2914855480 * b;
        l = l * l * l; m = m * m * m; s = s * s * s;
        double r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double bb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        return (Gam(r) * 255, Gam(g) * 255, Gam(bb) * 255);
    }

    private static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double Gam(double c)
    {
        c = Math.Clamp(c, 0, 1);
        return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
    }

    public static byte A(uint c) => (byte)(c >> 24);
    public static byte R(uint c) => (byte)(c >> 16);
    public static byte G(uint c) => (byte)(c >> 8);
    public static byte B(uint c) => (byte)c;
    public static uint Pack(byte a, byte r, byte g, byte b) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), 0, 255);
}
