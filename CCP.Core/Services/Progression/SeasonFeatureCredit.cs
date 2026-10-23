using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF <c>SeasonRecapService.TrackFeature</c> (ConditioningControlPanel/Services/Progression/
    /// SeasonRecapService.cs:192) and the <c>CurrentSeasonKey</c> it buckets under (:30), for heads that
    /// have no SeasonRecapService. The WPF head keeps its own copy (frozen); the logic is the same.
    /// </summary>
    public static class SeasonFeatureCredit
    {
        /// <summary>Note the per-day engagement event, then count the use in this season's bucket
        /// (Catalog features only, so a Lab launch never skews the recap bars).</summary>
        public static void Track(AppSettings? s, string featureKey, FeatureDayLogService? dayLog, DateTime utcNow)
        {
            var ev = SeasonFeatureKeys.ToDayLogEvent(featureKey);
            if (ev != null) dayLog?.Note(ev);
            if (SeasonFeatureKeys.Find(featureKey) == null || s == null) return;
            if (string.IsNullOrEmpty(s.SeasonStatsSeason)) s.SeasonStatsSeason = CurrentSeasonKey(s, utcNow);
            s.TrackSeasonFeature(featureKey);
        }

        /// <summary>The server's season when it is a yyyy-MM key, else the wall-clock month.</summary>
        public static string CurrentSeasonKey(AppSettings? s, DateTime utcNow)
        {
            var server = s?.CurrentSeason;
            return !string.IsNullOrWhiteSpace(server) && ProfileAdopt.LooksLikeMonthKey(server!)
                ? server!
                : utcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
