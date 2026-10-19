using System;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// When the "via ..." end card of an online clip comes up (owner, 2026-10-19). It used to wait
    /// for the clip's end and then sit over the frozen last frame; it now lays over the clip while
    /// the clip is ending, so the picture and the card finish together. The end is whichever comes
    /// first: the file's end, or the player's longest-video limit on a cut clip (that limit counts
    /// played time, so a random start does not move it). The host asks this on every time report
    /// and tells the page once. A clip with no known end keeps the old card after the end.
    /// Pure, pinned by FloatingVideoPolishTests.
    /// </summary>
    internal static class CreditOutroTiming
    {
        /// <param name="positionMs">Where the clip is in the file.</param>
        /// <param name="playedMs">How much of it has played (seeks and pauses excluded).</param>
        /// <param name="durationMs">The file's length, 0 when unknown.</param>
        /// <param name="capMs">The longest-video limit, 0 when none.</param>
        /// <param name="outroMs">How long the card shows.</param>
        public static bool ShouldStart(long positionMs, long playedMs, long durationMs, long capMs, int outroMs)
        {
            if (outroMs <= 0) return false;
            long remaining = long.MaxValue;
            if (durationMs > 0) remaining = durationMs - Math.Max(0, positionMs);
            if (capMs > 0) remaining = Math.Min(remaining, capMs - Math.Max(0, playedMs));
            if (remaining == long.MaxValue) return false;
            // A clip shorter than two cards keeps its first half clear of the card.
            long lead = Math.Min(outroMs, (Math.Max(0, playedMs) + Math.Max(0, remaining)) / 2);
            return remaining <= lead;
        }
    }
}
