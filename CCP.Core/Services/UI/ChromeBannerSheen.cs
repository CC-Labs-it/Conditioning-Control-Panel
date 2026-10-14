using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI;

/// <summary>The chrome banner's interaction cue: Reduced keeps it, Off drops it, and ordinary
/// four-second rotations only earn a pass every twenty seconds. New announcements bypass the gap.</summary>
public static class ChromeBannerSheen
{
    public static bool CanSweep(bool windowActive, MotionLevel motion, bool force,
        DateTime now, DateTime lastSweep) => windowActive && motion != MotionLevel.Off
        && (force || (now - lastSweep).TotalSeconds >= 20);
}
