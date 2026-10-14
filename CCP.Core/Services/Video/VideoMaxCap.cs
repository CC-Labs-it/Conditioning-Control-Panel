using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The user's max video length (<c>VideoMaxDurationSeconds</c>, "Duration filter" max) as a
    /// wall-clock cap, shared by both heads (BUG-7NAKK79CZ2: a 25-minute web video played under a
    /// 20-minute limit).
    /// </summary>
    public static class VideoMaxCap
    {
        /// <summary>A takeover may run this long past the clip's reported length (WPF #484).</summary>
        public const double TakeoverGraceSeconds = 15;
        /// <summary>Ceiling for a takeover with a known clip length.</summary>
        public const double TakeoverMaxSeconds = 3600;

        /// <summary>
        /// Seconds of the cap left after <paramref name="spentSeconds"/> of this clip were already on
        /// screen (a replay or engine fallback continues the clip's budget, it does not restart it).
        /// Null when no cap is set; never negative.
        /// </summary>
        public static double? CapLeft(int maxSeconds, double spentSeconds) =>
            maxSeconds > 0 ? Math.Max(0, maxSeconds - Math.Max(0, spentSeconds)) : null;

        /// <summary>
        /// When a browser web-video takeover must be force-ended, in seconds from now; null = no hard
        /// stop (the heartbeat governs). <paramref name="clipSeconds"/> &lt;= 0 means the length is
        /// unknown. A takeover the user started (they clicked the link) is bounded only by its clip;
        /// every other takeover (autonomy, AI, chaos, remote) also ends at the user's max, counted
        /// from <paramref name="sinceTakeoverStart"/> so a site auto-advancing to the next clip
        /// cannot restart it.
        /// </summary>
        public static double? TakeoverStopAfter(double clipSeconds, double sinceTakeoverStart, int maxSeconds, bool userStarted)
        {
            double? stop = clipSeconds > 0 ? Math.Min(clipSeconds + TakeoverGraceSeconds, TakeoverMaxSeconds) : null;
            if (userStarted || CapLeft(maxSeconds, sinceTakeoverStart) is not double left) return stop;
            return stop is double s ? Math.Min(s, left) : left;
        }
    }
}
