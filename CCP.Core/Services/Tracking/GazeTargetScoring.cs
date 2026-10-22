using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>A gaze target's geometry-only standing for one tick: Gaussian falloff plus its type prior,
    /// WITHOUT the sticky bonus. Bounds are in the same space as the gaze point.</summary>
    public readonly record struct GazeScore(double BaseScore, double X, double Y, double W, double H)
    {
        public double CentreX => X + W / 2.0;
        public double CentreY => Y + H / 2.0;
    }

    /// <summary>
    /// The target-scoring and refine-ambiguity rules of the gaze dwell engine, shared by both heads
    /// (moved out of WPF Services/Tracking/GazeFocusService.cs). Numbers in, numbers out: the head owns
    /// the targets, the clock and the refine panel.
    ///
    /// Predictive target scoring, replacing the old hard-radius slack hit-test. For each candidate:
    ///   score = exp(-d² / 2σ²)  (Gaussian falloff with distance d from rect edge)
    ///         + StickyBonus     (if target is the one we were already dwelling on)
    ///         + FlashTypeBonus  (flashes outrank background bubbles, foreground intent)
    /// The single highest score above ScoreThreshold wins. The Gaussian replaces the binary "inside
    /// slack / outside slack" cliff with a soft falloff, so small jitter at the boundary no longer
    /// toggles the lock. The sticky bonus prevents ping-pong when two targets are equidistant: noise
    /// has to push the cursor meaningfully closer to a *different* target before the lock switches.
    /// Sigmas are sized for quadrant-level reach: a bubble stays the acquired target out to ≈390 dips,
    /// roughly a quarter of a 1080p screen, so the cursor lock-on can draw the cursor in from its
    /// whole region.
    ///
    /// Two-stage zoom refine: a raw gaze point can never be precise (the fovea is ~1 degree and the eye
    /// micro-moves through every fixation, so the true point of regard lives inside an error blob of
    /// ~90-200px on a webcam rig). When two targets both sit in that blob with near-identical scores,
    /// taking the top score is a coin flip, so the head magnifies instead. Every threshold is set to
    /// under-trigger on purpose: a refine panel the user didn't need is far more annoying than an
    /// occasional wrong pick they can re-dwell. The rule demands ALL of:
    ///   * the winner is genuinely engaged (not a wisp at the threshold),
    ///   * the runner-up is genuinely engaged too,
    ///   * their scores are within RefineCloseRatio of each other,
    ///   * they are close enough together to plausibly share one error blob,
    ///   * and the winner did NOT need the sticky bonus to win (if it did, the user has already been
    ///     holding it for a whole dwell: that's evidence, not ambiguity). The head checks this last
    ///     clause against <see cref="AmbiguousLeader{T}"/>'s result.
    /// Ambiguity is judged on BASE scores: sticky exists to stop lock ping-pong, not to answer "which
    /// target did they mean".
    /// </summary>
    public static class GazeTargetScoring
    {
        public const double BubbleScoreSigma = 160; // ≈ 2.45σ (≈390 dips) at threshold
        public const double FlashScoreSigma = 90;
        public const double StickyBonus = 0.20;
        public const double FlashTypeBonus = 0.15;
        public const double ScoreThreshold = 0.05;

        public const double RefineCloseRatio = 0.85;        // runner-up within 15% of the winner
        public const double RefineMinWinnerScore = 0.30;    // ≈1.55σ — clearly in a target's region
        public const double RefineMinRunnerUpScore = 0.20;  // ≈1.79σ
        public const double RefineMaxSpreadDips = 280;      // centre-to-centre; wider than this is not one blob
        public const int MaxRefineCandidates = 4;

        public static double GaussianScore(double dist, double sigma)
        {
            var d = dist / sigma;
            return Math.Exp(-0.5 * d * d);
        }

        /// <summary>Perpendicular distance from (px, py) to the nearest edge of the rect; 0 inside it.</summary>
        public static double DistanceFromRectEdge(double x, double y, double w, double h, double px, double py)
        {
            var dx = Math.Max(0, Math.Max(x - px, px - (x + w)));
            var dy = Math.Max(0, Math.Max(y - py, py - (y + h)));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Flash base score: the tighter sigma plus the foreground type prior.</summary>
        public static double FlashBaseScore(double dist) => GaussianScore(dist, FlashScoreSigma) + FlashTypeBonus;

        /// <summary>Bubble and video attention-target base score: wide sigma, no type bonus, so a flash
        /// still wins when both are looked at.</summary>
        public static double BubbleBaseScore(double dist) => GaussianScore(dist, BubbleScoreSigma);

        /// <summary>
        /// Index of the highest-BASE-score candidate when it and the runner-up form a genuine ambiguity
        /// (both engaged, within RefineCloseRatio, centres within RefineMaxSpreadDips), else -1. The head
        /// refines only if that candidate is also the sticky-inclusive winner.
        /// </summary>
        public static int AmbiguousLeader<T>(IReadOnlyList<T> scored, Func<T, GazeScore> scoreOf)
        {
            if (scored.Count < 2) return -1;

            // Highest and second-highest BASE score, distinct candidates.
            int iA = -1, iB = -1;
            for (int i = 0; i < scored.Count; i++)
            {
                if (iA < 0 || scoreOf(scored[i]).BaseScore > scoreOf(scored[iA]).BaseScore) { iB = iA; iA = i; }
                else if (iB < 0 || scoreOf(scored[i]).BaseScore > scoreOf(scored[iB]).BaseScore) { iB = i; }
            }
            if (iA < 0 || iB < 0) return -1;

            var a = scoreOf(scored[iA]);
            var b = scoreOf(scored[iB]);
            if (a.BaseScore < RefineMinWinnerScore) return -1;
            if (b.BaseScore < RefineMinRunnerUpScore) return -1;
            if (a.BaseScore <= 0) return -1;
            if (b.BaseScore / a.BaseScore < RefineCloseRatio) return -1;

            // Both must plausibly live inside one gaze error blob. Two targets on opposite sides of the
            // screen scoring alike is a scoring artifact, not an ambiguity the user is experiencing.
            if (CentreDistance(a, b) > RefineMaxSpreadDips) return -1;
            return iA;
        }

        /// <summary>
        /// The refine panel's choice set, best first: every candidate that is genuinely engaged and in
        /// the same blob as the best one. The panel shows the real choice set, not just the top two: if
        /// three bubbles overlapped, hiding the third would make the refine itself feel wrong. Uncapped;
        /// the head stops at MaxRefineCandidates chips it could actually build.
        /// </summary>
        public static List<T> RefineChoices<T>(IReadOnlyList<T> scored, Func<T, GazeScore> scoreOf)
        {
            var ordered = new List<T>(scored);
            ordered.Sort((x, y) => scoreOf(y).BaseScore.CompareTo(scoreOf(x).BaseScore));
            var picked = new List<T>(ordered.Count);
            if (ordered.Count < 2) return picked;

            var head = scoreOf(ordered[0]);
            foreach (var s in ordered)
            {
                var g = scoreOf(s);
                if (g.BaseScore < RefineMinRunnerUpScore) continue;
                if (CentreDistance(g, head) > RefineMaxSpreadDips) continue;
                picked.Add(s);
            }
            return picked;
        }

        private static double CentreDistance(GazeScore a, GazeScore b)
        {
            var dx = a.CentreX - b.CentreX; var dy = a.CentreY - b.CentreY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
