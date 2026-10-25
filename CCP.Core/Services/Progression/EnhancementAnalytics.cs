using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF Enhancements PRO aggregation, shared by both heads. Snapshots arrive newest first.</summary>
public static class EnhancementAnalytics
{
    public static List<SeasonRecapSnapshot> ReadSnapshots(int cap = 24) => SeasonRecapStore.ListSeasonKeys()
        .Take(cap).Select(SeasonRecapStore.Load).OfType<SeasonRecapSnapshot>().ToList();

    public static string CurrentSeasonKey(AppSettings? s, DateTime utcNow) =>
        !string.IsNullOrWhiteSpace(s?.CurrentSeason) && ProfileAdopt.LooksLikeMonthKey(s.CurrentSeason!)
            ? s.CurrentSeason! : utcNow.ToString("yyyy-MM");

    public static string LiveKey(AppSettings s, string fallback) => s.SeasonStatsSeason ?? fallback;

    public static string FormatMinutes(double minutes) => minutes >= 60
        ? $"{minutes / 60:F1} {Loc.Get("label_hrs")}" : $"{minutes:F0} {Loc.Get("label_min_abbrev")}";

    public static string MonthLabel(string key) => SeasonNumbering.TryParse(key, out var year, out var month)
        ? new DateTime(year, month, 1).ToString("MMM", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant() : key;

    public static int PercentileFor(int rank, int total) => rank <= 0 || total <= 0 ? 0
        : Math.Clamp((int)Math.Ceiling((double)rank / total * 100), 1, 99);

    public static List<(string Key, double Minutes, int Level, List<string> ActiveDays)> Series(
        AppSettings s, string fallback, IEnumerable<SeasonRecapSnapshot> snapshots, int cap = 12)
    {
        var result = snapshots.Take(24).Select(x => (Key: x.SeasonKey, Minutes: x.SeasonMinutes,
            Level: x.PeakLevel, ActiveDays: x.ActiveDays ?? new List<string>())).ToList();
        var live = LiveKey(s, fallback);
        if (!result.Any(r => r.Key == live)) result.Add((live, s.SeasonConditioningMinutes,
            Math.Max(s.SeasonPeakLevel, s.PlayerLevel), new List<string>(s.SeasonActiveDays)));
        return result.OrderBy(r => r.Key, StringComparer.Ordinal).TakeLast(cap).ToList();
    }

    public static List<(string Key, double Minutes, int Sessions, int Days, int Streak, int Pct, int Level,
        int Spent, bool IsLive, bool HasV2)> Rewind(AppSettings s, string fallback, IEnumerable<SeasonRecapSnapshot> snapshots)
    {
        var live = LiveKey(s, fallback);
        var rows = new List<(string, double, int, int, int, int, int, int, bool, bool)>
        { (live, s.SeasonConditioningMinutes, s.SeasonSessionsStarted, s.SeasonActiveDays.Count,
            s.SeasonPeakStreak, PercentileFor(s.SeasonPeakRank, s.SeasonPeakRankTotal),
            Math.Max(s.SeasonPeakLevel, s.PlayerLevel), s.SeasonPointsSpent, true, true) };
        rows.AddRange(snapshots.Take(8).Where(x => x.SeasonKey != live).Select(x => (x.SeasonKey,
            x.SeasonMinutes, x.SessionCount, x.DaysActive, x.LongestStreak, x.Percentile, x.PeakLevel,
            x.PointsSpentSeason, false, x.Schema >= 2)));
        return rows;
    }

    public static List<(string Key, double Minutes, int Rank, int Level)> Records(AppSettings s,
        string fallback, IEnumerable<SeasonRecapSnapshot> snapshots)
    {
        var rows = new List<(string, double, int, int)> { (LiveKey(s, fallback),
            s.SeasonConditioningMinutes, s.SeasonPeakRank, Math.Max(s.SeasonPeakLevel, s.PlayerLevel)) };
        rows.AddRange(snapshots.Take(24).Select(x => (x.SeasonKey, x.SeasonMinutes, x.PeakRank, x.PeakLevel)));
        return rows;
    }

    public static (string Key, double Minutes, int Rank, int Level) BestTime(List<(string Key, double Minutes, int Rank, int Level)> seasons) =>
        seasons.OrderByDescending(s => s.Minutes).First();
    public static (string Key, double Minutes, int Rank, int Level)? BestRank(List<(string Key, double Minutes, int Rank, int Level)> seasons) =>
        seasons.Where(s => s.Rank > 0).OrderBy(s => s.Rank).Select(s => ((string, double, int, int)?)s).FirstOrDefault();
    public static (string Key, double Minutes, int Rank, int Level)? BestLevel(List<(string Key, double Minutes, int Rank, int Level)> seasons) =>
        seasons.Where(s => s.Level > 0).OrderByDescending(s => s.Level).Select(s => ((string, double, int, int)?)s).FirstOrDefault();
    public static IEnumerable<SeasonFeatureKeys.FeatureDef> OrderedFeatures(Dictionary<string, int> lifetime) =>
        SeasonFeatureKeys.Catalog.OrderByDescending(d => lifetime.GetValueOrDefault(d.Key));

    public static Dictionary<string, int> LifetimeFeatures(AppSettings s, IEnumerable<SeasonRecapSnapshot> snapshots)
    {
        var lifetime = new Dictionary<string, int>(s.SeasonFeatureUse);
        foreach (var snapshot in snapshots.Take(24))
            foreach (var kv in snapshot.FeatureUse)
                lifetime[kv.Key] = lifetime.GetValueOrDefault(kv.Key) + kv.Value;
        return lifetime;
    }
}
