using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF AchievementProgressStreak.cs:19-99,107-149,192-257.</summary>
internal static class LoginStreak
{
    // Return true when an insurance charge was consumed, so the head can report it to the server.
    internal static bool Advance(AchievementProgress p, AppSettings s, DateTime today, DateTime utcNow, bool resolve = false)
    {
        today = today.Date;
        if (!resolve) SeasonActivity.TrackStreakPeak(s, p.ConsecutiveDays, utcNow);
        if (p.PendingStreakBreak && !resolve) return false;
        if (resolve && !p.PendingStreakBreak) return false;
        p.PendingStreakBreak = false;
        var last = p.LastLaunchDate.Date;
        var spentInsurance = false;
        if (last == default)
        {
            p.ConsecutiveDays = Math.Max(1, p.ConsecutiveDays);
            p.LastLaunchDate = today;
        }
        else if (last == today) { }
        else if (last == today.AddDays(-1))
        {
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;
            p.LastLaunchDate = today;
        }
        else if (!resolve && !string.IsNullOrEmpty(s.UnifiedId) && !s.OfflineMode)
        {
            p.PendingStreakBreak = true;
            return false;
        }
        else
        {
            if (SkillTreeRules.HasSkill(s, "good_girl_streak") && s.StreakShieldsRemaining > 0)
            {
                s.StreakShieldsRemaining--;
                p.ConsecutiveDays++;
                p.PendingStreakBonus = true;
                for (var d = last.AddDays(1); d < today; d = d.AddDays(1))
                    if (!s.StreakShieldUsedDates.Contains(d)) s.StreakShieldUsedDates.Add(d);
            }
            else if (SkillTreeRules.HasSkill(s, "oopsie_insurance") && s.StreakFixCharges > 0)
            {
                s.StreakFixCharges--;
                s.SeasonalStreakRecoveryUsed = true;
                spentInsurance = true;
            }
            else p.ConsecutiveDays = 1;
            p.LastLaunchDate = today;
        }
        s.CurrentStreak = p.ConsecutiveDays;
        s.LastStreakDate = p.LastLaunchDate;
        SeasonActivity.TrackStreakPeak(s, p.ConsecutiveDays, utcNow);
        return spentInsurance;
    }

    // WPF SkillTreeService.cs:400-414.
    internal static int Bonus(int days) => days <= 0 ? 0 : days <= 3 ? 50 : days <= 6 ? 100
        : days <= 13 ? 150 : days <= 29 ? 200 : 300;

    internal static bool Adopt(AchievementProgress p, int streak, DateTime? cloudDate, DateTime today)
    {
        var adopted = AchievementProgress.DecideLoginStreakAdopt(p.ConsecutiveDays, p.LastLaunchDate, streak, cloudDate, today);
        if (adopted is not { } value) return false;
        p.ConsecutiveDays = value.Streak;
        if (value.LastDate != default) p.LastLaunchDate = value.LastDate;
        return true;
    }
}
