using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The user's max video length (<c>VideoMaxDurationSeconds</c>, "Duration filter" max) as the budget a
    /// replayed mandatory clip continues, shared by both heads (BUG-7NAKK79CZ2: a 25-minute web video played under a
    /// 20-minute limit).
    /// </summary>
    public static class VideoMaxCap
    {
        /// <summary>
        /// Seconds of the cap left after <paramref name="spentSeconds"/> of this clip were already on
        /// screen (a replay or engine fallback continues the clip's budget, it does not restart it).
        /// Null when no cap is set; never negative.
        /// </summary>
        public static double? CapLeft(int maxSeconds, double spentSeconds) =>
            maxSeconds > 0 ? Math.Max(0, maxSeconds - Math.Max(0, spentSeconds)) : null;
    }
}
