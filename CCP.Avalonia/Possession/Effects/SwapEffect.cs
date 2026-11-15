// WPF Services/Possession/Effects/SwapEffect.cs:136-194: partner geometry and preference.
using System;
using Avalonia;

namespace ConditioningControlPanel.Avalonia.Possession;

// The partner score is independent of layout parent. The effect and its safety checks are part B.
internal static class SwapPartnerRule
{
    internal static double Score(Rect origin, Rect candidate, bool originIsStartStop, bool candidateIsStartStop)
    {
        if (origin.Width <= 0 || origin.Height <= 0 || candidate.Width <= 0 || candidate.Height <= 0)
            return double.PositiveInfinity;
        double dx = candidate.Center.X - origin.Center.X;
        double dy = candidate.Center.Y - origin.Center.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(distance) || distance < 8 || distance > 220 ||
            Ratio(origin.Width, candidate.Width) > 1.6 || Ratio(origin.Height, candidate.Height) > 1.6)
            return double.PositiveInfinity;
        return distance - (originIsStartStop && candidateIsStartStop ? 10000 : 0);
    }

    private static double Ratio(double a, double b) => Math.Max(a, b) / Math.Min(a, b);
}
