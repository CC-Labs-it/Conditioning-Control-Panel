using System;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Turns a player's sparse position reports into a steady ~10 Hz playhead (ccp-bugs #1421).
    /// LibVLC and the browser page report the position only a few times a second, and the UI-thread
    /// hop bunches those up further. The Deeper player polls its own clock every 100 ms, so the
    /// same file lost its short effects only when it played as a mandatory video: a band the
    /// playhead crossed between two reports was never entered, and neighbours landed in one burst.
    ///
    /// Pure arithmetic over a caller-supplied millisecond clock, so the tests drive it by hand.
    /// Between reports the playhead runs on the wall clock, never further than
    /// <see cref="MaxLeadMs"/> past the last report (a stalled or buffering player stops it) and
    /// never backwards on its own: a report slightly behind what was already handed out is
    /// jitter, and the playhead waits for it to catch up. Only a real jump back is passed on.
    /// </summary>
    public sealed class PlayheadExtrapolator
    {
        /// <summary>How often the owner should call <see cref="OnTimer"/>.</summary>
        public const int TickMs = 100;
        /// <summary>The furthest the playhead runs past the last real report.</summary>
        public const int MaxLeadMs = 500;
        /// <summary>A report at most this far behind the playhead is jitter, not a seek.</summary>
        public const int JitterMs = 500;

        private long _anchorMs = -1;   // position the wall clock runs from
        private long _anchorAt;        // clock value at that position
        private long _emittedMs = -1;  // last position handed out

        /// <summary>A real report from the player. Returns the position to hand on, or -1 for none.</summary>
        public long OnReport(long positionMs, long nowMs)
        {
            if (positionMs < 0) return -1;
            _anchorMs = positionMs;
            _anchorAt = nowMs;
            if (_emittedMs >= 0 && positionMs <= _emittedMs && _emittedMs - positionMs <= JitterMs)
                return -1;
            _emittedMs = positionMs;
            return positionMs;
        }

        /// <summary>The owner's timer. Returns the position to hand on, or -1 for none.
        /// <paramref name="durationMs"/> is the media length when known, else 0.</summary>
        public long OnTimer(long nowMs, bool playing, long durationMs = 0)
        {
            if (_anchorMs < 0) return -1;
            if (!playing)
            {
                // Paused: hold where the playhead got to, and keep the anchor current so the
                // resume starts from there instead of leaping ahead by the length of the pause.
                if (_emittedMs > _anchorMs) _anchorMs = _emittedMs;
                _anchorAt = nowMs;
                return -1;
            }
            var lead = Math.Clamp(nowMs - _anchorAt, 0, MaxLeadMs);
            var estimate = _anchorMs + lead;
            if (durationMs > 0 && estimate > durationMs) estimate = durationMs;
            if (estimate <= _emittedMs) return -1;
            _emittedMs = estimate;
            return estimate;
        }
    }
}
