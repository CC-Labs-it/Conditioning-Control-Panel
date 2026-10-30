using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI;

/// <summary>Header drum travel from WPF BannerFxRules, independent of the drawing head.</summary>
public static class BannerDrum
{
    public const double Seconds = 0.5, Travel = 13, Squash = 0.3;

    public static (double Y, double Scale) Face(MotionLevel level, double seconds, bool incoming)
    {
        if (level != MotionLevel.Full || seconds >= Seconds) return (0, 1);
        double p = Math.Clamp(seconds / Seconds, 0, 1);
        // WPF QuadraticEase In on departure, BackEase Out (amplitude 0.3) on arrival.
        if (p == 0) return incoming ? (Travel, Squash) : (0, 1);
        double q = 1 - p;
        double eased = incoming ? 1 - q * q * q + 0.3 * q * Math.Sin(Math.PI * q) : p * p;
        return incoming
            ? (Travel * (1 - eased), Squash + (1 - Squash) * eased)
            : (-Travel * eased, 1 - (1 - Squash) * eased);
    }
}
