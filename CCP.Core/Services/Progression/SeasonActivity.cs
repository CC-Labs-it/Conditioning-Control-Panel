using System;
using System.Globalization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF SeasonRecapService.cs:146-165: the season active-day and peak-streak writers.
/// The login streak lifecycle itself is LoginStreakHead on the Avalonia head.</summary>
public static class SeasonActivity
{
    /// <summary>Adds today's UTC date once; the caller saves when this returns true, as WPF does.</summary>
    public static bool MarkActiveToday(AppSettings s, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, utcNow);
        var today = utcNow.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (s.SeasonActiveDays.Contains(today)) return false;
        s.SeasonActiveDays.Add(today);
        return true;
    }

    /// <summary>Raises the season peak; a streak reset never lowers it.</summary>
    public static bool TrackStreakPeak(AppSettings s, int days)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, DateTime.UtcNow);
        if (days <= s.SeasonPeakStreak) return false;
        s.SeasonPeakStreak = days;
        return true;
    }
}
