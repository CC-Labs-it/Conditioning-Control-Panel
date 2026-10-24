using System;

namespace ConditioningControlPanel.Services.Progression
{
    /// <summary>
    /// Conditioned minutes from the engine's 1 s timer, counted by the real time between ticks
    /// (beebee, 2026-10-21: a Leash "15 minutes" task missed after 15 minutes of engine).
    ///
    /// The timer used to count TICKS: 60 ticks = 1 minute. It is a Background-priority
    /// DispatcherTimer, so under lock cards, videos and a busy UI ticks arrive late and missed ones
    /// are dropped; a player's log (ccp-bugs #1489) shows ten ticks every 11-14.5 s, a quarter of the
    /// time lost. The Leash report, the day log and the quests all read the stored total.
    /// A gap is capped at <see cref="MaxGapSeconds"/> so a sleep or a frozen app does not count.
    /// Pure, pinned by ConditioningClockTests.
    /// </summary>
    public static class ConditioningClock
    {
        public const double MaxGapSeconds = 15;

        /// <summary>Adds one tick's gap to <paramref name="pendingSeconds"/> and returns the whole
        /// minutes now due, leaving the remainder pending.</summary>
        public static int Tick(ref double pendingSeconds, double gapSeconds)
        {
            if (double.IsFinite(gapSeconds) && gapSeconds > 0)
                pendingSeconds += Math.Min(gapSeconds, MaxGapSeconds);
            if (pendingSeconds < 60) return 0;
            var minutes = (int)(pendingSeconds / 60);
            pendingSeconds -= minutes * 60;
            return minutes;
        }
    }
}
