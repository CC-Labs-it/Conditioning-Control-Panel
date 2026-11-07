using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// ccp-bugs#1571: Bubble Count's clip counts toward "Watch N minutes of video" (quests and
    /// program tasks read the same QuestCategory.Video counter). It already did, but only when the
    /// clip reached its end, and then for the clip's nominal length: a game left early with Escape
    /// or panic credited nothing, and a clip that stalled credited time nobody watched.
    ///
    /// <para>Now the game credits REAL time: wall time from the first frame to the moment the
    /// video ends or the game closes, capped at the clip length, credited once per game. On the
    /// LibVLC path it is also capped at the furthest playback position the player reported
    /// (<see cref="NotePosition"/>), so a clip that stalls at 10 s and is ended by the safety timer
    /// credits 10 s, not the clip. The browser engine reports no position, so there a stall can
    /// still credit up to the clip length (the old behaviour, never more). Mandatory
    /// videos credit an interrupted clip the same way (#447). The interaction queue runs one video or
    /// one Bubble Count at a time (a video asked for mid-game is parked, ChaosVideoStartWindowTests),
    /// so the two never credit the same seconds.</para>
    ///
    /// <para>Pure (times are passed in, in seconds) so the rule is unit tested without a player.</para>
    /// </summary>
    internal sealed class BubbleCountWatchCredit
    {
        private double? _startedAt;
        private bool _credited;
        private double? _furthestPosition;

        /// <summary>The first frame is on screen. Later calls (a restart's second Playing) are ignored.</summary>
        public void MarkPlaying(double nowSeconds)
        {
            if (_startedAt == null && !_credited) _startedAt = nowSeconds;
        }

        /// <summary>The player's playback position (LibVLC TimeChanged). Only the furthest counts.</summary>
        public void NotePosition(double positionSeconds)
        {
            if (_credited || positionSeconds < 0) return;
            if (_furthestPosition == null || positionSeconds > _furthestPosition) _furthestPosition = positionSeconds;
        }

        /// <summary>
        /// Seconds to credit now, once: real time since the first frame, capped at the clip length
        /// when it is known and at the furthest reported position when there is one. Zero if the
        /// video never started or the game was already credited.
        /// </summary>
        public double Take(double nowSeconds, double clipSeconds)
        {
            if (_credited || _startedAt == null) return 0;
            _credited = true;
            var watched = Math.Max(0, nowSeconds - _startedAt.Value);
            if (clipSeconds > 0) watched = Math.Min(watched, clipSeconds);
            if (_furthestPosition != null) watched = Math.Min(watched, _furthestPosition.Value);
            return watched;
        }
    }
}
