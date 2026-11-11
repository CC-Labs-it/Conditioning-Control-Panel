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
}
