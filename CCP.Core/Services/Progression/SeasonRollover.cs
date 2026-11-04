using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>Captures the completed local bucket before advancing to a confirmed server season.</summary>
public static class SeasonRollover
{
    public static SeasonRecapSnapshot? CaptureAndRollover(AppSettings s, string? serverSeason,
        Func<SeasonRecapSnapshot, bool>? save = null)
    {
        if (string.IsNullOrWhiteSpace(serverSeason) || !ProfileAdopt.LooksLikeMonthKey(serverSeason)
            || !string.Equals(s.CurrentSeason, serverSeason, StringComparison.Ordinal)) return null;
        var ended = string.IsNullOrEmpty(s.SeasonStatsSeason)
            ? SeasonNumbering.Previous(serverSeason) : s.SeasonStatsSeason;
        if (!ProfileAdopt.LooksLikeMonthKey(ended) || string.CompareOrdinal(serverSeason, ended) <= 0) return null;

        var snap = new SeasonRecapSnapshot
        {
            SeasonKey = ended, CapturedAtUtc = DateTime.UtcNow,
            SeasonMinutes = s.SeasonConditioningMinutes, AllTimeMinutes = s.TotalConditioningMinutes,
            SessionCount = s.SeasonSessionsStarted, PeakRank = s.SeasonPeakRank,
            PeakRankTotal = s.SeasonPeakRankTotal,
            Percentile = EnhancementAnalytics.PercentileFor(s.SeasonPeakRank, s.SeasonPeakRankTotal),
            DaysActive = s.SeasonActiveDays.Count, SeasonLengthDays = SeasonNumbering.LengthDays(ended),
            LongestStreak = s.SeasonPeakStreak, HighestLevelEver = s.HighestLevelEver,
            IsOg = s.IsSeason0Og, FeatureUse = new Dictionary<string, int>(s.SeasonFeatureUse),
            FeaturesTotal = SeasonFeatureKeys.TotalCount, PeakLevel = Math.Max(s.SeasonPeakLevel, s.PlayerLevel),
            PointsSpentSeason = s.SeasonPointsSpent, SkillsOwned = s.UnlockedSkills?.Count ?? 0,
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
