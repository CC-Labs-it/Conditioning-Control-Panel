using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Layout and update rules of the run-pick ribbon (WPF <c>ChaosBoonBarOverlay</c>), shared by
/// every head: tiles are the run's <see cref="ChaosRunState.RunPickTiles"/> in pick order, only
/// tiles that arrived since the last rebuild pop in (staggered), and the ribbon is right-anchored
/// beside the wave-timer clock so it grows leftward.
/// </summary>
public static class ChaosBoonBarRules
{
    public const double Tile = 42;            // WPF ChaosBoonBarOverlay.cs:28
    public const double TileGap = 6;          // :202 Margin(0,0,6,0)
    public const double ClockReserve = 220;   // :29 strip kept clear for the wave-timer pill
    public const double TopInset = 10;        // :130 wa.Top + 10
    public const double StaggerMs = 70, PopMs = 340, FadeMs = 190, PopFrom = 0.4, PopAmplitude = 0.7;   // :163-169

    /// <summary>The ribbon's tiles: a snapshot of the picks in pick order (WPF :54).</summary>
    public static List<ChaosSidebarBoon> Snapshot(IEnumerable<ChaosSidebarBoon> picks) => new(picks);

    /// <summary>Pop-in order of tile <paramref name="index"/> when <paramref name="shownBefore"/>
    /// tiles were already on the ribbon; -1 = it was already shown, no animation (WPF :151).</summary>
    public static int PopOrder(int index, int shownBefore) => index >= shownBefore ? index - shownBefore : -1;

    /// <summary>Left edge (DIPs) of a ribbon <paramref name="width"/> wide: right-anchored left of the clock (WPF :132).</summary>
    public static double Left(double workAreaRight, double width) => workAreaRight - ClockReserve - width;

    /// <summary>Scale and opacity of a popping tile <paramref name="elapsedMs"/> after the rebuild
    /// (WPF :163-169: BackEase(0.7) out over 340 ms, fade over 190 ms, both delayed 70 ms per order).</summary>
    public static (double Scale, double Opacity) PopFrame(int order, double elapsedMs)
    {
        if (order < 0) return (1, 1);
        double t = elapsedMs - StaggerMs * order;
        double p = Math.Clamp(t / PopMs, 0, 1), f = Math.Clamp(t / FadeMs, 0, 1);
        return (PopFrom + (1 - PopFrom) * BackEaseOut(p, PopAmplitude), f);
    }

    /// <summary>True once every tile of a rebuild with <paramref name="newTiles"/> newcomers has landed.</summary>
    public static bool PopDone(int newTiles, double elapsedMs) =>
        newTiles <= 0 || elapsedMs >= StaggerMs * (newTiles - 1) + PopMs;

    /// <summary>WPF BackEase EaseOut: 1 - f(1 - t), f(x) = x^3 - x * A * sin(pi x).</summary>
    public static double BackEaseOut(double t, double amplitude)
    {
        double x = 1 - t;
        return 1 - (x * x * x - x * amplitude * Math.Sin(Math.PI * x));
    }
}
