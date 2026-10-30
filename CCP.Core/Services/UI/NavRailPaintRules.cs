using System;

namespace ConditioningControlPanel.Services.UI;

/// <summary>WPF NavRailRules' vivid hue and diagonal raised/socket bevel, in 0xAARRGGBB.</summary>
public static class NavRailPaintRules
{
    public const double VividLightness = 0.58, VividSaturation = 0.80;
    public const double RingBevelLight = 0.35, RingBevelShade = 0.50;
    public const byte RingIdleAlpha = 0xCC, RingHoverAlpha = 0xF2, RingActiveAlpha = 0xFF;
    public const double RingIdleThickness = 3.0, RingActiveThickness = 3.5;

    /// <summary>Wave 9: hue wash over the art, brighter on the selected row.</summary>
    public static uint ArtTint(uint hue, bool active) => NavStripPaint.WithAlphaByte(hue, active ? (byte)0x2E : (byte)0x14);

    public static uint Vivid(uint hue)
    {
        var (h, s, l) = NavStripPaint.ToHsl(hue);
        return NavStripPaint.WithAlphaByte(NavStripPaint.FromHsl(h, Math.Max(s, VividSaturation),
            Math.Min(l, VividLightness)), NavStripPaint.A(hue));
    }

    public static byte RingAlpha(bool active, bool hover) => active ? RingActiveAlpha : hover ? RingHoverAlpha : RingIdleAlpha;
    public static double RingThickness(bool active) => active ? RingActiveThickness : RingIdleThickness;

    public static (uint Color, double Offset)[] RingStops(uint hue, bool active, bool hover)
    {
        var vivid = Vivid(hue);
        byte alpha = RingAlpha(active, hover);
        var light = NavStripPaint.WithAlphaByte(NavStripPaint.Mix(vivid, NavStripPaint.White, RingBevelLight), alpha);
        var mid = NavStripPaint.WithAlphaByte(vivid, alpha);
        var shade = NavStripPaint.WithAlphaByte(NavStripPaint.Mix(vivid, 0xFF000000, RingBevelShade), alpha);
        return active ? new[] { (shade, 0.0), (mid, 0.5), (light, 1.0) }
                      : new[] { (light, 0.0), (mid, 0.42), (shade, 1.0) };
    }
}
