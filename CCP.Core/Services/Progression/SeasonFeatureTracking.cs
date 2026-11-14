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

        /// <summary>
        /// WPF SessionEngine.cs:296-318 and SeasonRecapService.IncrementSessionStarted (:161): once per
        /// session start (never on pause or resume), count the season's session and record each feature
        /// the session runs from its enabled flags. Spiral and Pink share one Overlay credit. No Save:
        /// the caller saves once.
        /// </summary>
        public static void RecordSessionStart(AppSettings s, SessionSettings session, Action<string>? noteDayLog, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(s.SeasonStatsSeason))
                s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, utcNow);
            s.SeasonSessionsStarted += 1;
            void Feature(string key) => Track(s, key, noteDayLog, utcNow);
            if (session.FlashEnabled) Feature(SeasonFeatureKeys.Flash);
            if (session.MandatoryVideosEnabled) Feature(SeasonFeatureKeys.Video);
            if (session.SubliminalEnabled) Feature(SeasonFeatureKeys.Subliminal);
            if (session.SpiralEnabled || session.PinkFilterEnabled) Feature(SeasonFeatureKeys.Overlay);
            if (session.BubblesEnabled) Feature(SeasonFeatureKeys.Bubbles);
            if (session.BubbleCountEnabled) Feature(SeasonFeatureKeys.BubbleCount);
            if (session.BouncingTextEnabled) Feature(SeasonFeatureKeys.BouncingText);
            if (session.LockCardEnabled) Feature(SeasonFeatureKeys.LockCard);
            if (session.MindWipeEnabled) Feature(SeasonFeatureKeys.MindWipe);
        }
    }
}
