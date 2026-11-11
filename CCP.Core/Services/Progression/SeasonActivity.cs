using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>SeasonRecapService.cs:122,146,159: active days and streak peak.</summary>
public static class SeasonActivity
{
    private static void EnsureBucket(AppSettings s, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, utcNow);
    }

    public static void MarkActiveToday(AppSettings s, DateTime utcNow, Action save)
    {
        EnsureBucket(s, utcNow);
        var today = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (s.SeasonActiveDays.Contains(today)) return;
        s.SeasonActiveDays.Add(today);
        save();
    }

    public static void TrackStreakPeak(AppSettings s, int streak, DateTime utcNow)
    {
        EnsureBucket(s, utcNow);
        if (streak > s.SeasonPeakStreak) s.SeasonPeakStreak = streak;
    }
}
