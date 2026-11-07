using System;

namespace ConditioningControlPanel.Services;

/// <summary>WPF MainWindow.Marquee.cs:1007-1034: a weekly reminder must earn its popup slot.</summary>
public static class IntakeNudgeRule
{
    public static bool ShouldOffer(bool enabled, bool passAvailable, string? dismissedWeek,
        string week, bool slotClaimed, bool sessionRunning, bool chaosRunning, bool gameOpen, bool intakeOpen)
        => enabled && passAvailable && !string.Equals(dismissedWeek, week, StringComparison.Ordinal)
            && !slotClaimed && !sessionRunning && !chaosRunning && !gameOpen && !intakeOpen;
}
