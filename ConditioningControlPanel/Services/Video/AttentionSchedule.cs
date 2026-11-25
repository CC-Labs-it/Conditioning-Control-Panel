using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// When the attention targets of one mandatory video spawn, and which spawns the end verdict
    /// may count (ccp-bugs #1736). Pure: plain values in, so tests never touch App.Settings.
    ///
    /// A target stays on screen for the user's lifespan setting. The old schedule stopped spawning
    /// at a fixed 5 s before the end (and its min-gap pass could push a spawn later still), so with
    /// the default 12 s lifespan a target could appear with a second or two left and the run was
    /// marked failed. Now no spawn lands later than <c>duration - lifespan - 1</c>, and a target the
    /// video cut short is never counted as a miss.
    /// </summary>
    internal static class AttentionSchedule
    {
        /// <summary>No target in the first seconds: the video is still coming up.</summary>
        public const double FirstSpawnSeconds = 3.0;

        /// <summary>Minimum gap between two spawns, so targets never land together.</summary>
        public const double MinGapSeconds = 3.0;

        /// <summary>Slack between a target's last second on screen and the video's end.</summary>
        public const double EndMarginSeconds = 1.0;

        /// <summary>The latest second a target may spawn and still get its whole lifespan.</summary>
        public static double LatestSpawn(double durationSeconds, double lifespanSeconds)
            => durationSeconds - Math.Max(0, lifespanSeconds) - EndMarginSeconds;

        /// <summary>
        /// Up to <paramref name="count"/> sorted spawn times (seconds from the start). Times are
        /// drawn in [<see cref="FirstSpawnSeconds"/>, <see cref="LatestSpawn"/>]; the min-gap pass
        /// may push a spawn later but never past the cap: a spawn that would go past it is dropped.
        /// A clip too short for even one full target gets no targets at all.
        /// </summary>
        /// <param name="nextDouble">A [0, 1) source, e.g. <c>Random.NextDouble</c>.</param>
        public static List<double> Plan(double durationSeconds, int count, double lifespanSeconds,
            Func<double> nextDouble, double minGapSeconds = MinGapSeconds)
        {
            var plan = new List<double>();
            if (count <= 0 || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds))
                return plan;

            var latest = LatestSpawn(durationSeconds, lifespanSeconds);
            if (double.IsNaN(latest) || latest < FirstSpawnSeconds)
                return plan;

            var window = latest - FirstSpawnSeconds;
            var drawn = new List<double>(count);
            for (int i = 0; i < count; i++)
                drawn.Add(FirstSpawnSeconds + nextDouble() * window);
            drawn.Sort();

            foreach (var raw in drawn)
            {
                var t = raw;
                if (plan.Count > 0 && t - plan[^1] < minGapSeconds)
                    t = plan[^1] + minGapSeconds;
                if (t > latest) continue; // never shift a target into the end; drop it
                plan.Add(t);
            }
            return plan;
        }

        /// <summary>
        /// How many spawns the verdict must not count as misses: still on screen, not answered,
        /// and with lifespan left when the video ended. Covers what the schedule cannot predict
        /// (duration unknown at plan time, a late length change, an early natural end).
        /// </summary>
        public static int CutShort(IEnumerable<(double DueElapsed, bool Answered)> spawns, double endedElapsed)
        {
            int n = 0;
            foreach (var (due, answered) in spawns)
                if (!answered && due > endedElapsed) n++;
            return n;
        }

        /// <summary>Spawns the verdict judges: every spawn minus the ones the end cut short.
        /// Zero means there was no real check, so there is no pass and no fail.</summary>
        public static int Judged(int spawned, int cutShort) => Math.Max(0, spawned - Math.Max(0, cutShort));

        /// <summary>Pass when every judged spawn was answered.</summary>
        public static bool Passed(int hits, int judged) => hits >= judged;
    }
}
