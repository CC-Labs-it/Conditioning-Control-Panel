using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI;

/// <summary>Clockwise thin-strip lift from WPF SectionEdgeRules (8f4bb7814, 3f94b09af).</summary>
public static class SectionEdgeMotion
{
    public const double LapSeconds = 12;

    // WPF SectionEdgeRules.MotionFor: a tier that refuses loops keeps the fixed lift.
    public static MotionLevel EffectiveLevel(MotionLevel level, bool ambientLoopsAllowed) => level switch
    {
        MotionLevel.Off => MotionLevel.Off,
        MotionLevel.Full when ambientLoopsAllowed => MotionLevel.Full,
        _ => MotionLevel.Reduced,
    };

    // Side order: top, right, bottom, left. Position is relative to the side's centre.
    public static double Position(int side, double seconds, MotionLevel level)
    {
        if (level != MotionLevel.Full) return side == 0 && level == MotionLevel.Reduced ? 0 : -1;
        double leg = Math.Clamp((seconds % LapSeconds - side * 3) / 3, 0, 1);
        return (side < 2 ? 1 : -1) * (-1 + 2 * leg);
    }

    public static bool ShouldRun(MotionLevel level, bool active, bool visible, bool minimized) =>
        level == MotionLevel.Full && active && visible && !minimized;
}
