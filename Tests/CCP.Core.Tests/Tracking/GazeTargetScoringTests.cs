using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.GazeTargetScoring;

namespace CCP.Core.Tests;

/// <summary>The gaze dwell engine's target scoring and refine-ambiguity rules, shared by both heads.</summary>
public class GazeTargetScoringTests
{
    private static GazeScore At(double score, double cx, double cy) => new(score, cx - 50, cy - 50, 100, 100);
    private static GazeScore Id(GazeScore s) => s;

    [Fact]
    public void Distance_is_zero_inside_and_measured_from_the_nearest_edge_outside()
    {
        Assert.Equal(0, DistanceFromRectEdge(0, 0, 100, 100, 50, 50));
        Assert.Equal(30, DistanceFromRectEdge(0, 0, 100, 100, 130, 50), 9);
        Assert.Equal(5, DistanceFromRectEdge(0, 0, 100, 100, 103, 104), 9);   // corner: 3-4-5
    }

    [Fact]
    public void Base_scores_peak_on_the_target_and_flashes_carry_the_type_prior()
    {
        Assert.Equal(1.0, BubbleBaseScore(0), 9);
        Assert.Equal(1.0 + FlashTypeBonus, FlashBaseScore(0), 9);
        // One sigma out the Gaussian is exp(-1/2).
        Assert.Equal(System.Math.Exp(-0.5), BubbleBaseScore(BubbleScoreSigma), 9);
        Assert.Equal(System.Math.Exp(-0.5) + FlashTypeBonus, FlashBaseScore(FlashScoreSigma), 9);
        // Quadrant reach: a bubble ~390 dips away is still just above the acquisition threshold.
        Assert.True(BubbleBaseScore(390) > ScoreThreshold);
        Assert.True(BubbleBaseScore(400) < ScoreThreshold);
    }

    [Fact]
    public void Two_engaged_close_near_equal_targets_are_ambiguous_and_the_leader_is_the_top_base_score()
    {
        var scored = new List<GazeScore> { At(0.5, 0, 0), At(0.9, 100, 0), At(0.8, 200, 0) };
        Assert.Equal(1, AmbiguousLeader(scored, Id));
    }

    [Fact]
    public void Ambiguity_needs_every_clause()
    {
        Assert.Equal(-1, AmbiguousLeader(new List<GazeScore> { At(0.9, 0, 0) }, Id));                     // one target
        Assert.Equal(-1, AmbiguousLeader(new List<GazeScore> { At(0.29, 0, 0), At(0.28, 50, 0) }, Id));   // winner a wisp
        // Runner-up wisp (< 0.20) cannot be reached alone: a winner >= 0.30 within 0.85 forces a runner-up >= 0.255,
        // so that clause is kept for WPF parity but is implied by the two around it.
        Assert.Equal(-1, AmbiguousLeader(new List<GazeScore> { At(0.9, 0, 0), At(0.7, 50, 0) }, Id));     // not close (0.78 < 0.85)
        Assert.Equal(-1, AmbiguousLeader(new List<GazeScore> { At(0.9, 0, 0), At(0.9, 281, 0) }, Id));    // not one blob
        Assert.Equal(0, AmbiguousLeader(new List<GazeScore> { At(0.9, 0, 0), At(0.9, 280, 0) }, Id));     // spread is inclusive
        Assert.Equal(0, AmbiguousLeader(new List<GazeScore> { At(1.0, 0, 0), At(0.85, 50, 0) }, Id));     // 0.85 exactly (exact in double)
        Assert.Equal(-1, AmbiguousLeader(new List<GazeScore> { At(1.0, 0, 0), At(0.849, 50, 0) }, Id));   // just under the ratio
    }

    [Fact]
    public void Refine_choices_are_best_first_and_drop_wisps_and_far_targets()
    {
        var far = At(0.95, 1000, 0);
        var head = At(0.9, 0, 0);
        var wisp = At(0.1, 10, 0);
        var near = At(0.6, 0, 200);
        var mid = At(0.8, 100, 0);
        // The far one is the best score, so it heads the set and everyone else is outside its blob.
        Assert.Equal(new[] { far }, RefineChoices(new List<GazeScore> { head, far, near }, Id));
        Assert.Equal(new[] { head, mid, near }, RefineChoices(new List<GazeScore> { near, wisp, head, mid }, Id));
        Assert.Empty(RefineChoices(new List<GazeScore> { head }, Id));
    }
}
