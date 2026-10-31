using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI;

// WPF NavRailRules coin motion, polish waves 10 and 11.
public static class NavCoinLaw
{
    public static double Tilt(double nx, double ny) => Math.Clamp(nx, -1, 1) * -Math.Clamp(ny, -1, 1) * DepthLaw.TiltDegrees;
    public static bool Tilts(bool active, MotionLevel level, PerformanceTier tier) =>
        !active && level != MotionLevel.Off && tier != PerformanceTier.Performance;
    public static double Overshoot(double from, double to) => to == from ? to : to + Math.Sign(to - from) * DepthLaw.ReleaseOvershootPx;
    public static double Travel(double from, double to, double progress, bool spring)
    {
        double p = Math.Clamp(progress, 0, 1);
        if (!spring) return from + (to - from) * (1 - (1 - p) * (1 - p));
        double over = Overshoot(from, to);
        if (p < 0.6) return Travel(from, over, p / 0.6, false);
        double u = (p - 0.6) / 0.4;
        double ease = u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2;
        return over + (to - over) * ease;
    }
}
