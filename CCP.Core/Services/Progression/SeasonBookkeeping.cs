using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF MainWindow.Marquee.cs:271-288 and SeasonRecapService.cs:216-313.</summary>
public static class SeasonBookkeeping
{
    // Account sync can update CurrentSeason off the UI thread while the tracker is writing.
    internal static readonly object Gate = new();

    public static void Update(AppSettings s, long? lifetimePointsSpent = null, int? leaderboardPercentile = null)
    {
        lock (Gate)
        {
            var month = s.CurrentSeason;
            var pendingTarget = s.PendingSeasonSnapshot != null ? s.PendingSeasonTarget : null;
            if (!string.IsNullOrWhiteSpace(pendingTarget) && ProfileAdopt.LooksLikeMonthKey(pendingTarget)
                && (string.IsNullOrWhiteSpace(month) || !ProfileAdopt.LooksLikeMonthKey(month)
                    || string.CompareOrdinal(pendingTarget, month) > 0)) month = pendingTarget;
            if (string.IsNullOrWhiteSpace(month) || !ProfileAdopt.LooksLikeMonthKey(month)) return;
            if (string.CompareOrdinal(month, s.LastSeasonResetSeen ?? "") > 0)
            {
                if (string.IsNullOrEmpty(s.SeasonStatsSeason)) s.SeasonStatsSeason = month;
                else if (string.CompareOrdinal(month, s.SeasonStatsSeason) > 0)
                {
                    s.PendingSeasonMinutes ??= new();
                    var points = lifetimePointsSpent ?? s.PendingSeasonLifetimePointsSpent;
                    var fallback = leaderboardPercentile ?? s.PendingSeasonPercentile;
                    // A failed save can span more than one confirmed season. Roll each pending bucket in order.
                    var targets = s.PendingSeasonMinutes.Keys.Where(k => ProfileAdopt.LooksLikeMonthKey(k)
                        && string.CompareOrdinal(k, s.SeasonStatsSeason) > 0 && string.CompareOrdinal(k, month) <= 0)
                        .Append(month).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
                    foreach (var target in targets)
                    {
                        var snap = s.PendingSeasonSnapshot ?? Snapshot(s, points, fallback);
                        bool meaningful = snap.SeasonMinutes > 0 || snap.SessionCount > 0 || snap.FeatureUse.Count > 0
                            || snap.LongestStreak > 0 || snap.DaysActive > 0 || snap.PeakRank > 0;
                        if (meaningful && !SeasonRecapStore.TrySave(snap))
                        {
                            s.PendingSeasonSnapshot = snap;
                            if (string.CompareOrdinal(month, s.PendingSeasonTarget) > 0) s.PendingSeasonTarget = month;
                            s.PendingSeasonPercentile = fallback;
                            s.PendingSeasonLifetimePointsSpent = points;
                            CoreSettings.Save();
                            return;
                        }
                        s.PendingSeasonSnapshot = null;
                        s.SeasonConditioningMinutes = 0;
                        s.SeasonSessionsStarted = 0;
                        s.SeasonActiveDays = new List<string>();
                        s.SeasonPeakStreak = 0;
                        s.SeasonPeakRank = 0;
                        s.SeasonPeakRankTotal = 0;
                        s.SeasonFeatureUse = new Dictionary<string, int>();
                        s.SeasonPeakLevel = 0;
                        s.SeasonPointsSpent = 0;
                        s.SeasonStatsSeason = target;
                        if (s.PendingSeasonMinutes.Remove(target, out var minutes)) s.SeasonConditioningMinutes = minutes;
                    }
                    s.PendingSeasonTarget = null;
                    s.PendingSeasonPercentile = 0;
                    s.PendingSeasonLifetimePointsSpent = 0;
                }
                s.LastSeasonResetSeen = month;
            }
            else if (!s.SeasonResetPending) return;
            s.SeasonResetPending = false;
            CoreSettings.Save();
        }
    }

    private static SeasonRecapSnapshot Snapshot(AppSettings s, long points, int fallback)
    {
        var percentile = EnhancementAnalytics.PercentileFor(s.SeasonPeakRank, s.SeasonPeakRankTotal);
        return new SeasonRecapSnapshot
        {
            SeasonKey = s.SeasonStatsSeason!, CapturedAtUtc = DateTime.UtcNow,
            Handle = s.UserDisplayName ?? "you", SeasonMinutes = s.SeasonConditioningMinutes,
            AllTimeMinutes = s.TotalConditioningMinutes - s.PendingSeasonMinutes.Values.Sum(),
            SessionCount = s.SeasonSessionsStarted, PeakRank = s.SeasonPeakRank, PeakRankTotal = s.SeasonPeakRankTotal,
            Percentile = percentile == 0 ? fallback : percentile,
            DaysActive = s.SeasonActiveDays.Count, SeasonLengthDays = SeasonNumbering.LengthDays(s.SeasonStatsSeason!),
            LongestStreak = s.SeasonPeakStreak, HighestLevelEver = s.HighestLevelEver,
            IsSupporter = CoreEntitlement.HasPremium, IsOg = s.IsSeason0Og,
            FeatureUse = new Dictionary<string, int>(s.SeasonFeatureUse), FeaturesTotal = SeasonFeatureKeys.TotalCount,
            PeakLevel = Math.Max(s.SeasonPeakLevel, s.PlayerLevel), PointsSpentSeason = s.SeasonPointsSpent,
            LifetimePointsSpent = points, SkillsOwned = s.UnlockedSkills?.Count ?? 0,
            ActiveDays = new List<string>(s.SeasonActiveDays),
        };
    }
}
