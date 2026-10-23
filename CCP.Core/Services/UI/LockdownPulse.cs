using System;

namespace ConditioningControlPanel.Services.UI;

/// <summary>WPF's linear, auto-reversing 1.5-second pink border/glow track.</summary>
public static class LockdownPulse
{
    public const double LegSeconds = 1.5;

    public static (byte R, byte G, byte B, double Blur, double Opacity) At(double seconds)
    {
        var phase = (double.IsFinite(seconds) ? Math.Max(0, seconds) : 0) / LegSeconds % 2;
        var weight = phase <= 1 ? phase : 2 - phase;
        return (255, Channel(20, 105, weight), Channel(147, 180, weight), 12 + 10 * weight, 0.7 + 0.3 * weight);
    }

    // WPF ColorAnimation interpolates ScRGB; interpolate in linear light, then encode sRGB bytes.
    private static byte Channel(byte from, byte to, double weight)
    {
        static double Linear(byte b)
        {
            var c = b / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var c = Linear(from) + (Linear(to) - Linear(from)) * weight;
        var encoded = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
    }
}
