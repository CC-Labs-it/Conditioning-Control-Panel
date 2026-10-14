using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>Pure PRO analytics moved from WPF MainWindow.Enhancements.cs; both heads use this copy.</summary>
public static class EnhancementAnalytics
{
    public static string FormatMinutes(double minutes) => minutes >= 60
        ? $"{minutes / 60:F1} {Loc.Get("label_hrs")}" : $"{minutes:F0} {Loc.Get("label_min_abbrev")}";

    public static string CurrentSeasonKey(AppSettings settings) => ProfileAdopt.LooksLikeMonthKey(settings.CurrentSeason ?? "")
        ? settings.CurrentSeason! : DateTime.UtcNow.ToString("yyyy-MM");

    public static string MonthLabel(string key) => SeasonNumbering.TryParse(key, out var y, out var m)
        ? new DateTime(y, m, 1).ToString("MMM", CultureInfo.InvariantCulture).ToLowerInvariant() : key;

    public static List<(string Key, double Minutes, int Level, List<string> ActiveDays)> Series(
        AppSettings settings, IEnumerable<SeasonRecapSnapshot> snapshots, string currentKey, int cap)
    {
        var result = snapshots.Select(s => (Key: s.SeasonKey, Minutes: s.SeasonMinutes, Level: s.PeakLevel,
            ActiveDays: s.ActiveDays ?? new List<string>())).ToList();
        var liveKey = settings.SeasonStatsSeason ?? currentKey;
        if (!result.Any(s => s.Key == liveKey))
            result.Add((liveKey, settings.SeasonConditioningMinutes, Math.Max(settings.SeasonPeakLevel, settings.PlayerLevel),
                new List<string>(settings.SeasonActiveDays)));
        return result.OrderBy(s => s.Key, StringComparer.Ordinal).TakeLast(cap).ToList();
    }

    public static List<(string Key, double Minutes, int Sessions, int Days, int Streak, int Pct, int Level,
        int Spent, bool IsLive, bool HasV2)> Rewind(AppSettings settings, IEnumerable<SeasonRecapSnapshot> snapshots, string currentKey)
    {
        var liveKey = settings.SeasonStatsSeason ?? currentKey;
        var rows = new List<(string Key, double Minutes, int Sessions, int Days, int Streak, int Pct, int Level,
            int Spent, bool IsLive, bool HasV2)>
        {
            (liveKey, settings.SeasonConditioningMinutes, settings.SeasonSessionsStarted, settings.SeasonActiveDays.Count,
                settings.SeasonPeakStreak, Percentile(settings.SeasonPeakRank, settings.SeasonPeakRankTotal),
                Math.Max(settings.SeasonPeakLevel, settings.PlayerLevel), settings.SeasonPointsSpent, true, true)
        };
        foreach (var s in snapshots)
            if (s.SeasonKey != liveKey)
                rows.Add((s.SeasonKey, s.SeasonMinutes, s.SessionCount, s.DaysActive, s.LongestStreak, s.Percentile,
                    s.PeakLevel, s.PointsSpentSeason, false, s.Schema >= 2));
        return rows;
    }

    public static int Percentile(int rank, int total) => rank <= 0 || total <= 0 ? 0
        : Math.Min(99, Math.Max(1, (int)Math.Ceiling((double)rank / total * 100)));

    public static List<(string Key, double Minutes, int Rank, int Level)> Records(
        AppSettings settings, IEnumerable<SeasonRecapSnapshot> snapshots, string currentKey)
    {
        var result = new List<(string Key, double Minutes, int Rank, int Level)>
        {
            (settings.SeasonStatsSeason ?? currentKey, settings.SeasonConditioningMinutes, settings.SeasonPeakRank,
                Math.Max(settings.SeasonPeakLevel, settings.PlayerLevel))
        };
        result.AddRange(snapshots.Select(s => (s.SeasonKey, s.SeasonMinutes, s.PeakRank, s.PeakLevel)));
        return result;
    }

    public static ((string Key, double Minutes, int Rank, int Level) Time,
        (string Key, double Minutes, int Rank, int Level) Rank,
        (string Key, double Minutes, int Rank, int Level) Level) BestRecords(
        AppSettings settings, IEnumerable<SeasonRecapSnapshot> snapshots, string currentKey)
    {
        var seasons = Records(settings, snapshots, currentKey);
        return (seasons.OrderByDescending(s => s.Minutes).First(),
            seasons.Where(s => s.Rank > 0).OrderBy(s => s.Rank).FirstOrDefault(),
            seasons.Where(s => s.Level > 0).OrderByDescending(s => s.Level).FirstOrDefault());
    }

    public static Dictionary<string, int> LifetimeFeatureUse(AppSettings settings, IEnumerable<SeasonRecapSnapshot> snapshots)
    {
        var lifetime = new Dictionary<string, int>(settings.SeasonFeatureUse);
        foreach (var s in snapshots)
            foreach (var kv in s.FeatureUse)
            {
                lifetime.TryGetValue(kv.Key, out var n);
                lifetime[kv.Key] = n + kv.Value;
            }
        return lifetime;
    }
}
