using System;

namespace ConditioningControlPanel.Services.Timing
{
    /// <summary>
    /// The real time between two timer ticks, for clocks that used to add a fixed TickMs per tick.
    ///
    /// <para>WHY: a <c>DispatcherTimer</c> (or a <c>Task.Delay</c> loop) never fires early and
    /// usually fires late. At the default Windows timer resolution (15.6 ms) a 33 ms timer lands
    /// every ~47 ms and a 50 ms timer every ~62 ms, and a busy UI (webcam tracking, videos,
    /// overlays) adds more. Counting "one tick = TickMs" then runs SLOWER than the wall clock the
    /// same code uses for its deadline: the attention check's 1 s stare took ~1.4 s against a
    /// wall-clock grace, the Lab gaze game's 3 s pass took ~3.75 s inside a 4 s window. Same class
    /// as the conditioning minutes in ccp-bugs #1489 (ConditioningClock).</para>
    ///
    /// <para>CAP: one step credits at most <c>maxMs</c>. A gaze dwell reads a single sample per
    /// tick, so a UI stall of two seconds must not turn one on-target sample into two seconds of
    /// stare. Pure, pinned by TickStepTests.</para>
    /// </summary>
    public static class TickStep
    {
        /// <summary>Most one tick may credit to a gaze dwell (several normal late ticks, never a stall).</summary>
        public const double DwellMaxStepMs = 200;

        /// <summary>
        /// Real milliseconds since <paramref name="lastUtc"/>, between 0 and <paramref name="maxMs"/>,
        /// and moves <paramref name="lastUtc"/> to <paramref name="nowUtc"/>. Zero when the stamp is
        /// unset (the first tick only opens it) or the clock went backwards.
        /// </summary>
        public static double Ms(ref DateTime lastUtc, DateTime nowUtc, double maxMs)
        {
            var last = lastUtc;
            lastUtc = nowUtc;
            if (last == default) return 0;
            var gap = (nowUtc - last).TotalMilliseconds;
            if (!(gap > 0) || !(maxMs > 0)) return 0;
            return Math.Min(gap, maxMs);
        }

        /// <summary>The same step on a millisecond counter (Core BubbleAccuracyTest.Dwell's time since
        /// the bubble showed, where 0 is a real start, not "unset"): between 0 and
        /// <paramref name="maxMs"/>, zero when the counter went backwards.</summary>
        public static double Ms(ref long lastMs, long nowMs, double maxMs)
        {
            var gap = nowMs - lastMs;
            lastMs = nowMs;
            if (gap <= 0 || !(maxMs > 0)) return 0;
            return Math.Min(gap, maxMs);
        }
    }
}
