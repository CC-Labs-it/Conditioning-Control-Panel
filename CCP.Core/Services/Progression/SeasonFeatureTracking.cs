using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>SessionEngine.cs:284-299 and SeasonRecapService.cs:119-126,183-196.</summary>
public static class SeasonFeatureTracking
{
    public static void RecordSession(AppSettings settings, SessionSettings session, Action<string>? note = null)
    {
        lock (SeasonBookkeeping.Gate)
        {
            if (string.IsNullOrEmpty(settings.SeasonStatsSeason))
                settings.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(settings, DateTime.UtcNow);
            settings.SeasonSessionsStarted++;
            // A failed rollover has already frozen its snapshot. Keep later credits in that
            // same bucket until the save succeeds, just as EnsureBucket does in WPF.
            var pending = settings.PendingSeasonSnapshot;
            if (pending != null) pending.SessionCount++;
            void Track(string key)
            {
                var wire = SeasonFeatureKeys.ToDayLogEvent(key);
                if (wire != null) note?.Invoke(wire);
                if (SeasonFeatureKeys.Find(key) != null)
                {
                    settings.TrackSeasonFeature(key);
                    if (pending != null)
                        pending.FeatureUse[key] = pending.FeatureUse.GetValueOrDefault(key) + 1;
                }
            }
            if (session.FlashEnabled) Track(SeasonFeatureKeys.Flash);
            if (session.MandatoryVideosEnabled) Track(SeasonFeatureKeys.Video);
            if (session.SubliminalEnabled) Track(SeasonFeatureKeys.Subliminal);
            if (session.SpiralEnabled || session.PinkFilterEnabled) Track(SeasonFeatureKeys.Overlay);
            if (session.BubblesEnabled) Track(SeasonFeatureKeys.Bubbles);
            if (session.BubbleCountEnabled) Track(SeasonFeatureKeys.BubbleCount);
            if (session.BouncingTextEnabled) Track(SeasonFeatureKeys.BouncingText);
            if (session.LockCardEnabled) Track(SeasonFeatureKeys.LockCard);
            if (session.MindWipeEnabled) Track(SeasonFeatureKeys.MindWipe);
        }
    }
}
