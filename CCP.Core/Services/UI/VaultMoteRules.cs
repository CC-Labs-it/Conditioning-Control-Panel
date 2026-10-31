using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI;

public static class VaultMoteRules
{
    /// <summary>Hard ceiling on motes alive at once, before the tier's own budget.</summary>
    public static int Cap(MotionLevel level) => level switch
    {
        MotionLevel.Full => 64,
        MotionLevel.Reduced => 14,
        _ => 0,
    };

    /// <summary>New motes a second while the page is short of its cap.</summary>
    public static double SpawnPerSecond(MotionLevel level) => level switch
    {
        MotionLevel.Full => 26,
        MotionLevel.Reduced => 4,
        _ => 0,
    };

    /// <summary>Rise speed multiplier: Reduced drifts at half pace.</summary>
    public static double SpeedScale(MotionLevel level) => level == MotionLevel.Full ? 1.0 : 0.5;

    /// <summary>Share of the motes that rise from a card or sign (the rest drift anywhere).</summary>
    public const double ZoneShare = 0.78;

    /// <summary>How far outside a zone's edge a mote may be born (pixels).</summary>
    public const double RingOut = 12;

    /// <summary>How far inside a zone's edge a mote may be born: the cards hide what is behind
    /// them, so a mote born deep inside would never show.</summary>
    public const double RingIn = 4;

    public const double LifeMin = 1.6, LifeMax = 3.4;

    /// <summary>A birth point on the ring around the zone: pick a side weighted by
    /// its length, a spot along it, and a depth across the edge. u1..u3 are 0..1 randoms.</summary>
    public static (double X, double Y) SpawnOnRing(double x, double y, double width, double height, double u1, double u2, double u3)
    {
        double w = Math.Max(1, width), h = Math.Max(1, height);
        double across = -RingIn + u3 * (RingIn + RingOut);   // negative = inside
        double t = u1 * (2 * w + 2 * h);
        if (t < w) return (x + u2 * w, y - across);
        t -= w;
        if (t < w) return (x + u2 * w, (y + height) + across);
        t -= w;
        if (t < h) return (x - across, y + u2 * h);
        return ((x + width) + across, y + u2 * h);
    }

    /// <summary>The motes the page keeps alive: the level's cap, never above twice the tier's
    /// particle budget (the governor halves that budget under pressure).</summary>
    public static int Target(MotionLevel level, int liveBudget) =>
        Math.Max(0, Math.Min(Cap(level), liveBudget * 2));
}
