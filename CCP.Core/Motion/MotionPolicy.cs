using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Motion;

/// <summary>WPF MotionFx.ResolveLevel: the OS can remove motion, never add it.</summary>
public static class MotionPolicy
{
    public static MotionLevel ResolveLevel(MotionLevel setting, bool osAnimationsEnabled)
    {
        if (setting == MotionLevel.Off) return MotionLevel.Off;
        return osAnimationsEnabled ? setting : MotionLevel.Reduced;
    }
}
