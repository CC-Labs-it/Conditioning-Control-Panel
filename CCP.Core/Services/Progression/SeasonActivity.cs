using System;
using System.Globalization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF SeasonRecapService.cs:146-165.</summary>
public static class SeasonActivity
{
    // The caller saves when this returns true, as WPF does after adding a day.
    public static bool MarkActiveToday(AppSettings s, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, utcNow);
        var today = utcNow.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (s.SeasonActiveDays.Contains(today)) return false;
        s.SeasonActiveDays.Add(today);
        return true;
    }

    public static void TrackStreakPeak(AppSettings s, int days)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, DateTime.UtcNow);
        if (days > s.SeasonPeakStreak) s.SeasonPeakStreak = days;
    }

    // WPF AchievementProgressStreak.cs:19-99, :105-150 and :192-257.
    // Return true when the caller must persist; a deferred gap deliberately keeps its old date.
    public static bool UpdateStreak(AppSettings s, AchievementProgress p, DateTime today, bool canDefer, bool resolve = false, bool allowProtection = true, Action<string, int>? moment = null)
    {
        today = today.Date;
        void Fire(string name, int streak) { try { moment?.Invoke(name, streak); } catch { } }
        TrackStreakPeak(s, p.ConsecutiveDays);
        if (resolve ? !p.PendingStreakBreak : p.PendingStreakBreak) return false;
        var last = p.LastLaunchDate.Date;
        if (resolve) p.PendingStreakBreak = false;
        if (last == default)
        {
            p.ConsecutiveDays = Math.Max(1, p.ConsecutiveDays);
        }
        else if (last == today)
        {
            s.CurrentStreak = p.ConsecutiveDays;
            s.LastStreakDate = p.LastLaunchDate;
            return resolve;
        }
        else if (last == today.AddDays(-1))
        {
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true; if (!resolve) Fire("streakKept", p.ConsecutiveDays);
        }
        else if (!resolve && canDefer)
        {
            p.PendingStreakBreak = true;
            return false;
        }
        else if (allowProtection && SkillTreeRules.HasSkill(s, "good_girl_streak") && s.StreakShieldsRemaining > 0)
        {
            s.StreakShieldsRemaining--;
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true; Fire("streakKept", p.ConsecutiveDays);
            for (var d = last.AddDays(1); d < today; d = d.AddDays(1))
                if (!s.StreakShieldUsedDates.Contains(d)) s.StreakShieldUsedDates.Add(d);
        }
        else if (allowProtection && SkillTreeRules.HasSkill(s, "oopsie_insurance") && s.StreakFixCharges > 0)
        {
            s.StreakFixCharges--;
            s.SeasonalStreakRecoveryUsed = true; Fire("streakKept", p.ConsecutiveDays);
        }
        else { Fire("streakBroken", p.ConsecutiveDays); p.ConsecutiveDays = 1; }
        p.LastLaunchDate = today;
        s.CurrentStreak = p.ConsecutiveDays;
        s.LastStreakDate = today;
        TrackStreakPeak(s, p.ConsecutiveDays);
        return true;
    }

    // WPF ProfileSyncService.cs:3478-3504. A malformed pair is not a cloud streak.
    public static bool AdoptStreak(AppSettings s, AchievementProgress p, Newtonsoft.Json.Linq.JObject? stats, DateTime today)
    {
        if (stats == null || !int.TryParse(stats["consecutive_days"]?.ToString(), out var days)) return false;
        var rawDate = stats["last_streak_date"];
        DateTime? date = rawDate?.Type == Newtonsoft.Json.Linq.JTokenType.Date
            ? rawDate.ToObject<DateTimeOffset>().Date
            : DateTime.TryParseExact(rawDate?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed : DateTimeOffset.TryParseExact(rawDate?.ToString(), new[] { "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) ? timestamp.Date : null;
        if (date > today.Date.AddDays(1)) date = null;
        var adopt = AchievementProgress.DecideLoginStreakAdopt(p.ConsecutiveDays, p.LastLaunchDate, days, date, today);
        if (adopt == null) return false;
        p.ConsecutiveDays = adopt.Value.Streak;
        if (adopt.Value.LastDate != default) p.LastLaunchDate = adopt.Value.LastDate;
        s.CurrentStreak = p.ConsecutiveDays;
        s.LastStreakDate = p.LastLaunchDate;
        TrackStreakPeak(s, p.ConsecutiveDays);
        return true;
    }
}
