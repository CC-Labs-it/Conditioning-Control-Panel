using System;
using System.Linq;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1736: attention targets spawned so close to a mandatory video's end that the user
/// could not answer them, and the run was marked failed. The schedule now keeps every spawn at or
/// before duration - lifespan - 1 s, and the end verdict skips a target the end cut short.
/// </summary>
public class AttentionScheduleTests
{
    private static Func<double> Seeded(int seed) => new Random(seed).NextDouble;

    /// <summary>A source that always returns the top of [0, 1): every draw lands on the cap.</summary>
    private static double AlmostOne() => 0.999999;

    [Theory]
    [InlineData(60.0, 12)]   // default lifespan
    [InlineData(60.0, 5)]    // preset lifespans
    [InlineData(60.0, 6)]
    [InlineData(45.0, 30)]   // the longest the setting allows
    [InlineData(300.0, 12)]
    public void No_spawn_lands_after_duration_minus_lifespan_minus_one(double dur, int lifespan)
    {
        var cap = dur - lifespan - 1;
        for (int seed = 0; seed < 200; seed++)
        {
            var plan = AttentionSchedule.Plan(dur, 10, lifespan, Seeded(seed));
            Assert.All(plan, t => Assert.InRange(t, AttentionSchedule.FirstSpawnSeconds, cap));
        }
    }

    [Fact]
    public void Default_lifespan_on_a_minute_clip_caps_at_47_seconds()
    {
        Assert.Equal(47.0, AttentionSchedule.LatestSpawn(60, 12));
        var plan = AttentionSchedule.Plan(60, 1, 12, AlmostOne);
        Assert.Single(plan);
        Assert.True(plan[0] <= 47.0);
        Assert.True(plan[0] + 12 <= 60 - 1); // the target lives its whole lifespan before the end
    }

    [Fact]
    public void A_custom_lifespan_moves_the_cap()
    {
        var shortLife = AttentionSchedule.Plan(30, 1, 5, AlmostOne);
        var longLife = AttentionSchedule.Plan(30, 1, 20, AlmostOne);
        Assert.True(shortLife.Single() <= 24.0);
        Assert.True(longLife.Single() <= 9.0);
        Assert.True(longLife.Single() < shortLife.Single());
    }

    [Fact]
    public void The_min_gap_pass_never_pushes_a_spawn_past_the_cap()
    {
        // Every draw lands at the cap, so the gap pass would push all but the first later.
        var plan = AttentionSchedule.Plan(60, 8, 12, AlmostOne);
        Assert.Single(plan);
        Assert.True(plan[0] <= AttentionSchedule.LatestSpawn(60, 12));
    }

    [Fact]
    public void Spawns_keep_the_min_gap()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var plan = AttentionSchedule.Plan(120, 10, 12, Seeded(seed));
            for (int i = 1; i < plan.Count; i++)
                Assert.True(plan[i] - plan[i - 1] >= AttentionSchedule.MinGapSeconds - 1e-9);
        }
    }

    [Fact]
    public void A_crowded_window_drops_targets_instead_of_shifting_them_into_the_end()
    {
        // 60 s, lifespan 12: spawns fit in [3, 47], at most 15 with a 3 s gap.
        var plan = AttentionSchedule.Plan(60, 40, 12, Seeded(7));
        Assert.True(plan.Count <= 15);
        Assert.True(plan.Count > 0);
        Assert.True(plan.Last() <= 47.0);
    }

    [Theory]
    [InlineData(15.0, 12)]   // 15 - 12 - 1 = 2 < 3: no room for one full target
    [InlineData(10.0, 12)]
    [InlineData(20.0, 30)]
    [InlineData(4.0, 1)]
    [InlineData(0.0, 12)]
    public void A_clip_too_short_for_one_full_target_gets_none(double dur, int lifespan)
        => Assert.Empty(AttentionSchedule.Plan(dur, 5, lifespan, Seeded(1)));

    [Fact]
    public void A_short_clip_with_room_for_exactly_one_gets_it_at_three_seconds()
    {
        // 16 - 12 - 1 = 3: the window is a single point.
        var plan = AttentionSchedule.Plan(16, 5, 12, Seeded(3));
        Assert.Equal(new[] { 3.0 }, plan);
    }

    [Fact]
    public void Junk_input_plans_nothing()
    {
        Assert.Empty(AttentionSchedule.Plan(double.NaN, 3, 12, Seeded(1)));
        Assert.Empty(AttentionSchedule.Plan(double.PositiveInfinity, 3, 12, Seeded(1)));
        Assert.Empty(AttentionSchedule.Plan(60, 0, 12, Seeded(1)));
    }

    [Fact]
    public void An_unanswered_target_with_lifespan_left_at_the_end_is_not_a_miss()
    {
        // Video ended at 58 s. Spawn A was answered, B expired unanswered at 40 s (gone from the
        // list in the app, but a stale entry must still count as a miss), C is on screen until 65 s.
        var spawns = new[] { (DueElapsed: 30.0, Answered: true), (40.0, false), (65.0, false) };
        var cutShort = AttentionSchedule.CutShort(spawns, endedElapsed: 58);
        Assert.Equal(1, cutShort);

        // 3 spawned, 1 hit: B is a real miss, so still a fail.
        var judged = AttentionSchedule.Judged(3, cutShort);
        Assert.Equal(2, judged);
        Assert.False(AttentionSchedule.Passed(1, judged));
    }

    [Fact]
    public void Answering_everything_the_user_could_see_passes()
    {
        // Two answered, one cut short by the end: the old rule (hits >= spawned) failed this run.
        var spawns = new[] { (DueElapsed: 20.0, Answered: true), (35.0, true), (70.0, false) };
        var judged = AttentionSchedule.Judged(3, AttentionSchedule.CutShort(spawns, endedElapsed: 60));
        Assert.Equal(2, judged);
        Assert.True(AttentionSchedule.Passed(2, judged));
    }

    [Fact]
    public void An_answered_target_with_lifespan_left_is_not_forgiven_twice()
    {
        var spawns = new[] { (DueElapsed: 70.0, Answered: true) };
        Assert.Equal(0, AttentionSchedule.CutShort(spawns, endedElapsed: 60));
        Assert.True(AttentionSchedule.Passed(1, AttentionSchedule.Judged(1, 0)));
    }

    [Fact]
    public void A_target_whose_lifespan_ran_out_exactly_at_the_end_is_a_miss()
    {
        var spawns = new[] { (DueElapsed: 60.0, Answered: false) };
        Assert.Equal(0, AttentionSchedule.CutShort(spawns, endedElapsed: 60));
    }

    [Fact]
    public void Only_cut_short_targets_means_no_verdict()
    {
        // One spawn, still live at the end: nothing was really judged, so neither pass nor fail.
        var judged = AttentionSchedule.Judged(1, AttentionSchedule.CutShort(new[] { (70.0, false) }, 60));
        Assert.Equal(0, judged);
    }
}
