using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

// WPF SkillTreeService.cs:710,573 and MainWindow.StartStop.cs:310-311.
public static class SkillTreeEngineStart
{
    public static void Apply(AppSettings settings, DateTime localNow, DateTime utcNow)
    {
        if (SkillTreeRules.HasSkill(settings, "good_girl_streak") &&
            (utcNow.Date - (settings.LastStreakShieldResetDate ?? DateTime.MinValue)).TotalDays >= 7)
        {
            settings.StreakShieldsRemaining = 1;
            settings.LastStreakShieldResetDate = utcNow.Date;
        }
        if (localNow.Hour >= 23 || localNow.Hour < 5) settings.NightTimeUsageCount += 2;
        if (localNow.Hour >= 5 && localNow.Hour < 8) settings.EarlyMorningUsageCount += 2;
    }
}
