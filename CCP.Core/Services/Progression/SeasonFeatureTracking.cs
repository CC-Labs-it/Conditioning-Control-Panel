using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The feature-use hook of WPF <c>SeasonRecapService</c> (TrackFeature :183, CurrentSeasonKey :30,
    /// EnsureBucket :122). The Avalonia head uses this hook; WPF retains its existing orchestration.
    /// </summary>
    public static class SeasonFeatureTracking
    {
        /// <summary>
        /// One use of a feature: the per-day log event (keys with a wire name only), then the season
        /// bucket (Catalog keys only), opening the bucket on first use. No Save, as WPF.
        /// </summary>
        public static void Track(AppSettings? s, string featureKey, Action<string>? noteDayLog, DateTime utcNow)
        {
            var ev = SeasonFeatureKeys.ToDayLogEvent(featureKey);
            if (ev != null) noteDayLog?.Invoke(ev);

            if (SeasonFeatureKeys.Find(featureKey) == null || s == null) return;
            if (string.IsNullOrEmpty(s.SeasonStatsSeason))
                s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, utcNow);
            s.TrackSeasonFeature(featureKey);
        }
    }
}
