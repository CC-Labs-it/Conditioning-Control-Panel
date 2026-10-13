using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The maths behind E-Stim's lightning and its cursor halo, lifted from the WPF windows in
/// ConditioningControlPanel/Chaos/ChaosEStimOverlay.cs so every head draws the same bolt.
/// Pure: no clock, no screen, no RNG of its own (the caller hands one in, tests a fixed one).
/// </summary>
public static class ChaosEStimMath
{
    public const int FlickerMs = 40;              // WPF FLICKER_MS: re-jitter cadence while hot
    public const int HotMs = 120;                 // WPF HOT_MS: full-brightness window
    public const int FadeMs = 80;                 // WPF FADE_MS: fade after the hot window
    public const double Jitter = 14;              // WPF JITTER: max sideways offset per midpoint (DIPs)
    public const double SegLen = 55;              // WPF SEG_LEN: DIPs between midpoints
    public const double FlashSize = 28;           // WPF FLASH_SIZE: impact flash diameter
    public const double StrikeOpacity = 0.85;     // WPF STRIKE_OPACITY: whole-strike ceiling
    public const double HaloSize = 64;            // WPF ChaosEStimGlow.HALO_SIZE
    public const double HaloWindow = 92;          // WPF ChaosEStimGlow.WIN_SIZE (DIPs)
    public const int HaloPulseMs = 160;           // WPF pulse: 0.88 -> 1.10, 160 ms, auto-reverse, sine in/out
    public const double HaloScaleMin = 0.88, HaloScaleMax = 1.10;

    /// <summary>One jagged bolt from A to B (WPF JitterBolts): A, max(2, len/SegLen) midpoints
    /// pushed sideways by up to <see cref="Jitter"/>, then B. <paramref name="next01"/> returns [0,1).</summary>
    public static (double X, double Y)[] BoltPath(double ax, double ay, double bx, double by, Func<double> next01)
    {
        double dx = bx - ax, dy = by - ay;
        double len = Math.Sqrt(dx * dx + dy * dy);
        int mids = Math.Max(2, (int)(len / SegLen));
        double px = len > 0.001 ? -dy / len : 0, py = len > 0.001 ? dx / len : 0;
        var pts = new (double X, double Y)[mids + 2];
        pts[0] = (ax, ay);
        for (int m = 1; m <= mids; m++)
        {
            double f = m / (double)(mids + 1);
            double off = (next01() * 2 - 1) * Jitter;
            pts[m] = (ax + dx * f + px * off, ay + dy * f + py * off);
        }
        pts[mids + 1] = (bx, by);
        return pts;
    }

    /// <summary>Strike opacity <paramref name="elapsedMs"/> after it began (WPF life keyframes):
    /// the ceiling through the hot window, linear to 0 over the fade, null once it is over (hide).</summary>
    public static double? StrikeOpacityAt(double elapsedMs)
    {
        if (elapsedMs < HotMs) return StrikeOpacity;
        if (elapsedMs >= HotMs + FadeMs) return null;
        return StrikeOpacity * (1 - (elapsedMs - HotMs) / FadeMs);
    }

    /// <summary>Which flicker the bolt shows (WPF StrikeTick re-jitters at 0, 40, 80 ms and stops
    /// once 120 is reached); the caller re-jitters when this changes. -1 once the bolt is frozen.</summary>
    public static int FlickerStep(double elapsedMs)
    {
        int step = (int)(Math.Max(0, elapsedMs) / FlickerMs);
        return step * FlickerMs >= HotMs ? -1 : step;
    }

    /// <summary>Physical screen pixels to window-local DIPs (WPF TransformFromDevice minus Left/Top).</summary>
    public static (double X, double Y) ToLocal(double px, double py, double originPxX, double originPxY, double scale)
    {
        double s = scale > 0 ? scale : 1;
        return ((px - originPxX) / s, (py - originPxY) / s);
    }

    /// <summary>Halo window top-left in physical pixels so the halo centres on the cursor
    /// (WPF FollowTick: cursor - WIN_SIZE/2, done in DIPs there; physical here).</summary>
    public static (int X, int Y) HaloOrigin(int cursorPxX, int cursorPxY, double scale)
    {
        double s = scale > 0 ? scale : 1;
        int half = (int)Math.Round(HaloWindow * s / 2);
        return (cursorPxX - half, cursorPxY - half);
    }

    /// <summary>Halo scale <paramref name="elapsedMs"/> into the pulse: sine in/out between
    /// <see cref="HaloScaleMin"/> and <see cref="HaloScaleMax"/>, one leg per <see cref="HaloPulseMs"/>.</summary>
    public static double HaloScaleAt(double elapsedMs)
        => HaloScaleMin + (HaloScaleMax - HaloScaleMin) * (1 - Math.Cos(Math.PI * elapsedMs / HaloPulseMs)) / 2;
}
