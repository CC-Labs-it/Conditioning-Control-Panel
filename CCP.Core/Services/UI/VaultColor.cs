using System;

namespace ConditioningControlPanel.Services.UI;

/// <summary>The vault accent pair, shared by both heads. Tier livery and daily-free gold are separate.</summary>
public static class VaultColor
{
    public const double PartnerHueShift = -59.0;

    /// <summary>WPF ShiftHue: rotate HSV hue while preserving saturation and value.</summary>
    public static (byte R, byte G, byte B) ShiftHue(byte red, byte green, byte blue, double degrees)
    {
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h;
        if (delta <= 0.0) h = 0;                                   // grey: hue is arbitrary
        else if (max == r) h = 60 * (((g - b) / delta) % 6);
        else if (max == g) h = 60 * (((b - r) / delta) + 2);
        else h = 60 * (((r - g) / delta) + 4);

        h = (h + degrees) % 360;
        if (h < 0) h += 360;

        double v = max;
        double s = max <= 0 ? 0 : delta / max;

        double chroma = v * s;
        double x = chroma * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - chroma;

        (double R, double G, double B) rgb = h switch
        {
            < 60 => (chroma, x, 0),
            < 120 => (x, chroma, 0),
            < 180 => (0, chroma, x),
            < 240 => (0, x, chroma),
            < 300 => (x, 0, chroma),
            _ => (chroma, 0, x),
        };

        static byte Byte(double v01) => (byte)Math.Clamp(Math.Round((v01) * 255), 0, 255);
        return (Byte(rgb.R + m), Byte(rgb.G + m), Byte(rgb.B + m));
    }
}
