using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>Vault HSV hue rotation from WPF MainWindow.Exclusives.cs; colors are 0xAARRGGBB.
    /// Both heads wrap this calculation in their native Color type.</summary>
    public static class VaultHue
    {
        public static uint Partner(uint accent) => ShiftHue(accent, -59.0);

        /// <summary>Preserves saturation, value and alpha, including achromatic colors.</summary>
        public static uint ShiftHue(uint c, double degrees)
        {
            double r = NavStripPaint.R(c) / 255.0, g = NavStripPaint.G(c) / 255.0, b = NavStripPaint.B(c) / 255.0;
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
            return NavStripPaint.Argb(NavStripPaint.A(c), Byte(rgb.R + m), Byte(rgb.G + m), Byte(rgb.B + m));
        }
    }
}
