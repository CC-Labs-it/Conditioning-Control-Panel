using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Video;

/// <summary>
/// "Fill the minimum with other videos" (AppSettings.MandatoryVideoFillShortWithOthers, off by
/// default), the sibling of <see cref="VideoMinLoop"/>. A full-screen mandatory clip shorter than
/// the minimum is followed by other random clips from the local pool, one after another, until the
/// minimum has played; then the run ends once, the way any video ends: one attention verdict, one
/// reward or penalty, never one per clip.
/// <para>
/// The host plans the order (<see cref="Plan"/>) from clips whose length it already knows, so the
/// safety timer can allow for every planned clip. The browser page (player.js 'ended') plays the
/// plan in order and stops as soon as the minimum is reached or the plan runs out. With no other
/// clip to use (a pool of one), or on LibVLC, the clip loops in place instead (VideoMinLoop).
/// </para>
/// </summary>
public static class VideoMinFill
{
    /// <summary>Upper bound on planned clips, so a pool of 1 s clips cannot build a huge message.</summary>
    public const int MaxClips = 64;

    /// <summary>Same surfaces as the loop: full-screen only, never Chaos segments, Deeper
    /// enhancements or credited (Scrolller) clips.</summary>
    public static bool Applies(bool enabled, int minSeconds, bool floating = false, bool segment = false,
        bool enhanced = false, bool credited = false)
        => VideoMinLoop.Applies(enabled, minSeconds, floating, segment, enhanced, credited);

    /// <summary>
    /// The order of the clips that follow the first one. Each pick differs from the clip before it
    /// whenever the pool has another; every candidate is used once before any repeats (a shuffled
    /// bag). Stops once the planned clips reach the minimum. Candidates with no known length, or
    /// equal to the first clip, are ignored. Empty = nothing to fill with (the caller loops instead).
    /// </summary>
    /// <param name="firstPath">The clip the run starts with.</param>
    /// <param name="firstMs">Its length in ms, 0 when unknown (the plan then covers the whole minimum).</param>
    public static List<(string Path, long Ms)> Plan(string firstPath, long firstMs, int minSeconds,
        IEnumerable<(string Path, long Ms)> candidates, Random random, int maxClips = MaxClips)
    {
        var plan = new List<(string Path, long Ms)>();
        if (minSeconds <= 0 || random == null || candidates == null) return plan;
        long minMs = minSeconds * 1000L;
        long played = Math.Max(0, firstMs);
        if (played >= minMs) return plan;

        // The first clip may come back later in the run (A, B, A, B with a pool of two), just
        // never straight after itself.
        var pool = new List<(string Path, long Ms)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(firstPath) && firstMs > 0 && seen.Add(firstPath)) pool.Add((firstPath, firstMs));
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c.Path) || c.Ms <= 0) continue;
            if (seen.Add(c.Path)) pool.Add(c);
        }
        bool hasOther = false;
        foreach (var p in pool)
            if (!string.Equals(p.Path, firstPath, StringComparison.OrdinalIgnoreCase)) { hasOther = true; break; }
        if (!hasOther) return plan;   // nothing different to show: the caller loops the clip instead

        string previous = firstPath;
        var bag = new List<(string Path, long Ms)>();
        while (played < minMs && plan.Count < maxClips)
        {
            if (bag.Count == 0)
            {
                bag.AddRange(pool);
                Shuffle(bag, random);
            }
            // Every candidate once before any repeats; never the clip that just played. The pool
            // holds at least two different clips, so a fresh bag always has one to give.
            int pick = bag.FindIndex(b => !string.Equals(b.Path, previous, StringComparison.OrdinalIgnoreCase));
            if (pick < 0) { bag.Clear(); continue; }
            var next = bag[pick];
            bag.RemoveAt(pick);
            plan.Add(next);
            played += next.Ms;
            previous = next.Path;
        }
        return plan;
    }

    /// <summary>Seconds the safety timer must allow for: the first clip plus every planned clip.</summary>
    public static double SafetySeconds(double firstSeconds, IReadOnlyList<(string Path, long Ms)> plan)
    {
        double total = Math.Max(0, firstSeconds);
        if (plan != null) foreach (var p in plan) total += p.Ms / 1000.0;
        return total;
    }

    /// <summary>
    /// The page's rule at the end of a clip (player.js 'ended'): move on to the next planned clip
    /// while less than the minimum has played and the plan has one left.
    /// </summary>
    public static bool NextAfterClip(long untilMs, long playedMs, int nextIndex, int planCount)
        => untilMs > 0 && playedMs < untilMs && nextIndex < planCount;

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
