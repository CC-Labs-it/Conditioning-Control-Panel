using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>Captures the completed local bucket before advancing to a confirmed server season.</summary>
public static class SeasonRollover
{
    /// <summary>WPF MainWindow.Marquee:270: silent monthly bookkeeping for a confirmed server key.</summary>
    public static bool UpdateBookkeeping(AppSettings s, Func<SeasonRecapSnapshot, bool>? save = null,
        string? handle = null, bool isSupporter = false, long lifetimePointsSpent = 0, int fallbackPercentile = 0)
    {
        var month = s.CurrentSeason;
        if (string.IsNullOrWhiteSpace(month) || !ProfileAdopt.LooksLikeMonthKey(month)) return false;
        if (string.CompareOrdinal(month, s.LastSeasonResetSeen ?? "") > 0)
        {
            if (string.IsNullOrEmpty(s.SeasonStatsSeason)) s.SeasonStatsSeason = month;
            else
            {
                CaptureAndRollover(s, month, save, handle, isSupporter, lifetimePointsSpent, fallbackPercentile);
                // A failed snapshot save must leave the rollover eligible for retry.
                if (string.CompareOrdinal(s.SeasonStatsSeason, month) < 0) return false;
            }
            s.LastSeasonResetSeen = month;
        }
        else if (!s.SeasonResetPending) return false;
        s.SeasonResetPending = false;
        return true;
    }

    public static SeasonRecapSnapshot? CaptureAndRollover(AppSettings s, string? serverSeason,
        Func<SeasonRecapSnapshot, bool>? save = null, string? handle = null, bool isSupporter = false,
        long lifetimePointsSpent = 0, int fallbackPercentile = 0)
    {
        if (string.IsNullOrWhiteSpace(serverSeason) || !ProfileAdopt.LooksLikeMonthKey(serverSeason)
            || !string.Equals(s.CurrentSeason, serverSeason, StringComparison.Ordinal)) return null;
        var ended = string.IsNullOrEmpty(s.SeasonStatsSeason)
            ? SeasonNumbering.Previous(serverSeason) : s.SeasonStatsSeason;
        if (!ProfileAdopt.LooksLikeMonthKey(ended) || string.CompareOrdinal(serverSeason, ended) <= 0) return null;

        var percentile = EnhancementAnalytics.PercentileFor(s.SeasonPeakRank, s.SeasonPeakRankTotal);
        var snap = new SeasonRecapSnapshot
        {
            SeasonKey = ended, CapturedAtUtc = DateTime.UtcNow, Handle = handle ?? "you",
            SeasonMinutes = s.SeasonConditioningMinutes, AllTimeMinutes = s.TotalConditioningMinutes,
            SessionCount = s.SeasonSessionsStarted, PeakRank = s.SeasonPeakRank,
            PeakRankTotal = s.SeasonPeakRankTotal,
            Percentile = percentile == 0 ? fallbackPercentile : percentile,
            DaysActive = s.SeasonActiveDays.Count, SeasonLengthDays = SeasonNumbering.LengthDays(ended),
            LongestStreak = s.SeasonPeakStreak, HighestLevelEver = s.HighestLevelEver,
            IsSupporter = isSupporter, IsOg = s.IsSeason0Og, FeatureUse = new Dictionary<string, int>(s.SeasonFeatureUse),
            FeaturesTotal = SeasonFeatureKeys.TotalCount, PeakLevel = Math.Max(s.SeasonPeakLevel, s.PlayerLevel),
            PointsSpentSeason = s.SeasonPointsSpent, LifetimePointsSpent = lifetimePointsSpent,
            SkillsOwned = s.UnlockedSkills?.Count ?? 0,
            ActiveDays = new List<string>(s.SeasonActiveDays)
        };
        bool meaningful = snap.SeasonMinutes > 0 || snap.SessionCount > 0 || snap.FeatureUse.Count > 0
            || snap.LongestStreak > 0 || snap.DaysActive > 0 || snap.PeakRank > 0;
        if (meaningful && !(save ?? SeasonRecapStore.TrySave)(snap)) return null;
        s.SeasonConditioningMinutes = 0;
        s.SeasonSessionsStarted = 0;
        s.SeasonActiveDays = new List<string>();
        s.SeasonPeakStreak = 0;
        s.SeasonPeakRank = 0;
        s.SeasonPeakRankTotal = 0;
        s.SeasonFeatureUse = new Dictionary<string, int>();
        s.SeasonPeakLevel = 0;
        s.SeasonPointsSpent = 0;
        s.SeasonStatsSeason = serverSeason;
        return meaningful ? snap : null;
    }
}
