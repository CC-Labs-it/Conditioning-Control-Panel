using System;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>WPF Services/Descent/DescentFuseChrome: four 11% steps toward ceremony black.
/// The palette's one writer applies this only to neutrals, preserving alpha and the mod's accents.</summary>
internal static class DescentFuseChrome
{
    internal static Color Dim(Color color, int step)
    {
        if (step <= 0) return color;
        var fraction = Math.Clamp(step, 1, 4) * 0.11;
        byte Lerp(byte from, byte to) => (byte)Math.Clamp(Math.Round(from + (to - from) * fraction), 0, 255);
        return Color.FromArgb(color.A, Lerp(color.R, 0x0A), Lerp(color.G, 0x05), Lerp(color.B, 0x14));
    }
}
