using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF MainWindow.Marquee.cs:271-288 and SeasonRecapService.cs:216-313.</summary>
public static class SeasonBookkeeping
{
    // Account sync can update CurrentSeason off the UI thread while the tracker is writing.
    internal static readonly object Gate = new();

    public static void Update(AppSettings s, long lifetimePointsSpent = 0)
    {
        lock (Gate)
        {
            var month = s.CurrentSeason;
            if (string.IsNullOrWhiteSpace(month) || !ProfileAdopt.LooksLikeMonthKey(month)) return;
            if (string.CompareOrdinal(month, s.LastSeasonResetSeen ?? "") > 0)
            {
                if (string.IsNullOrEmpty(s.SeasonStatsSeason)) s.SeasonStatsSeason = month;
                else if (string.CompareOrdinal(month, s.SeasonStatsSeason) > 0)
                {
                    var snap = new SeasonRecapSnapshot
                    {
                        SeasonKey = s.SeasonStatsSeason, CapturedAtUtc = DateTime.UtcNow,
                        Handle = s.UserDisplayName ?? "you", SeasonMinutes = s.SeasonConditioningMinutes,
                        AllTimeMinutes = s.TotalConditioningMinutes, SessionCount = s.SeasonSessionsStarted,
                        PeakRank = s.SeasonPeakRank, PeakRankTotal = s.SeasonPeakRankTotal,
                        Percentile = EnhancementAnalytics.PercentileFor(s.SeasonPeakRank, s.SeasonPeakRankTotal),
                        DaysActive = s.SeasonActiveDays.Count, SeasonLengthDays = SeasonNumbering.LengthDays(s.SeasonStatsSeason),
                        LongestStreak = s.SeasonPeakStreak, HighestLevelEver = s.HighestLevelEver,
                        IsSupporter = CoreEntitlement.HasPremium, IsOg = s.IsSeason0Og,
                        FeatureUse = new Dictionary<string, int>(s.SeasonFeatureUse), FeaturesTotal = SeasonFeatureKeys.TotalCount,
                        PeakLevel = Math.Max(s.SeasonPeakLevel, s.PlayerLevel), PointsSpentSeason = s.SeasonPointsSpent,
                        LifetimePointsSpent = lifetimePointsSpent, SkillsOwned = s.UnlockedSkills?.Count ?? 0,
                        ActiveDays = new List<string>(s.SeasonActiveDays),
                    };
                    bool meaningful = snap.SeasonMinutes > 0 || snap.SessionCount > 0 || snap.FeatureUse.Count > 0
                        || snap.LongestStreak > 0 || snap.DaysActive > 0 || snap.PeakRank > 0;
                    // Unlike WPF's void Save, retain the live bucket if persistence fails.
                    if (meaningful && !SeasonRecapStore.TrySave(snap)) return;
                    s.SeasonConditioningMinutes = 0;
                    s.SeasonSessionsStarted = 0;
                    s.SeasonActiveDays = new List<string>();
                    s.SeasonPeakStreak = 0;
                    s.SeasonPeakRank = 0;
                    s.SeasonPeakRankTotal = 0;
                    s.SeasonFeatureUse = new Dictionary<string, int>();
                    s.SeasonPeakLevel = 0;
                    s.SeasonPointsSpent = 0;
                    s.SeasonStatsSeason = month;
                }
                s.LastSeasonResetSeen = month;
            }
            else if (!s.SeasonResetPending) return;
            s.SeasonResetPending = false;
            CoreSettings.Save();
        }
    }
}
